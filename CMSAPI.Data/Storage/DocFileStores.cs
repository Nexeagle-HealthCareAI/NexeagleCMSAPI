using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CMSAPI.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data.Storage;

/// <summary>
/// Keeps uploaded documents in the S3-compatible store 1RADAPI uses (E2E Object Storage / MinIO), in a
/// folder of the CMS's own inside a bucket ("cms-docs/docs/…" in 1RAD's bucket, by default).
///
/// Settings (environment names in brackets). The first four are the same ones 1RADAPI reads:
///   Storage:PublicBaseUrl      [Storage__PublicBaseUrl]      the store's address
///   Storage:Minio:AccessKey    [Storage__Minio__AccessKey]
///   Storage:Minio:SecretKey    [Storage__Minio__SecretKey]
///   Storage:Minio:Bucket       [Storage__Minio__Bucket]      1RAD's bucket; used when DocsBucket is not set
///   Storage:Minio:DocsBucket   [Storage__Minio__DocsBucket]  a bucket of the CMS's own (optional)
///   Storage:Minio:DocsFolder   [Storage__Minio__DocsFolder]  default "cms-docs"; "" for the bucket's top
///   Storage:Minio:Region       [Storage__Minio__Region]      default "us-east-1"
///
/// A folder in object storage is only the start of each file's name, so nothing has to be made in the
/// console. Files are written with no public access of their own and are only ever read by this API,
/// after it has checked who is asking; no link to the store is handed to a browser. The bucket's own
/// policy must not make this folder public (1RAD shares only its downloads/ folder).
/// </summary>
public class MinioDocFileStore : IDocFileStore
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _folder;   // "cms-docs/" or "" — put before every key
    private readonly string _host;

    public MinioDocFileStore(IConfiguration configuration)
    {
        var endpoint = configuration["Storage:PublicBaseUrl"] ?? configuration["Storage:Minio:PublicBaseUrl"];
        var accessKey = configuration["Storage:Minio:AccessKey"];
        var secretKey = configuration["Storage:Minio:SecretKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("MINIO_CONFIG_MISSING: set Storage:PublicBaseUrl, Storage:Minio:AccessKey and Storage:Minio:SecretKey.");

        var bucket = configuration["Storage:Minio:DocsBucket"];
        if (string.IsNullOrWhiteSpace(bucket)) bucket = configuration["Storage:Minio:Bucket"];
        _bucket = string.IsNullOrWhiteSpace(bucket) ? "cms-docs" : bucket.Trim();
        _folder = FolderPrefix(configuration["Storage:Minio:DocsFolder"] ?? "cms-docs");
        _host = Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ? uri.Host : endpoint.Trim();

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint.Trim().TrimEnd('/'),   // the scheme decides http or https
            ForcePathStyle = true,                // MinIO needs path-style addresses
            AuthenticationRegion = configuration["Storage:Minio:Region"] ?? "us-east-1",
        };
        _s3 = new AmazonS3Client(accessKey, secretKey, config);
    }

    public bool IsConfigured => true;

    /// <summary>Which store and bucket, for the start-up log. No keys.</summary>
    public string Describe() => _folder.Length == 0 ? $"bucket '{_bucket}' at {_host}" : $"folder '{_folder}' in bucket '{_bucket}' at {_host}";

    /// <summary>"cms-docs" / "/cms-docs/" / " cms-docs " → "cms-docs/"; empty → "" (the bucket's top).</summary>
    public static string FolderPrefix(string? folder)
    {
        var clean = (folder ?? string.Empty).Trim().Trim('/');
        return clean.Length == 0 ? string.Empty : clean + "/";
    }

    public async Task SaveAsync(string key, Stream content, string contentType)
    {
        // PutObject needs to know the length, and a second try needs to read the file again, so a
        // stream that cannot seek is copied to memory first.
        var stream = content;
        MemoryStream? buffer = null;
        if (!content.CanSeek)
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            buffer.Position = 0;
            stream = buffer;
        }
        var start = stream.Position;

        Task Put() => _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = _folder + key,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false,   // the caller owns the stream it gave us
        });

        try
        {
            try
            {
                await Put();
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                // First upload on a new store: make the (private) bucket, then try once more.
                await CreateBucketAsync(ex);
                stream.Position = start;
                await Put();
            }
        }
        catch (AmazonS3Exception ex)
        {
            throw Explain(ex);
        }
        catch (AmazonServiceException ex)
        {
            throw new DocStorageException($"The file store at {_host} refused the file ({ex.ErrorCode ?? ((int)ex.StatusCode).ToString()}).", ex);
        }
        catch (Exception ex) when (ex is AmazonClientException or HttpRequestException)
        {
            throw new DocStorageException($"The file store at {_host} cannot be reached. Check Storage__PublicBaseUrl and that the store is running.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new DocStorageException($"The file store at {_host} did not answer in time. Try again in a minute.", ex);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    private async Task CreateBucketAsync(AmazonS3Exception missing)
    {
        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket, UseClientRegion = true });
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou")
        {
            // Made by another upload a moment ago.
        }
        catch (AmazonS3Exception ex)
        {
            throw new DocStorageException(
                $"The bucket '{_bucket}' does not exist on {_host}, and these storage keys may not create it ({ex.ErrorCode}). " +
                "Create it in the storage console (private), or set Storage__Minio__DocsBucket to a bucket that exists.", missing);
        }
    }

    /// <summary>The store's refusal in words an administrator can act on.</summary>
    private DocStorageException Explain(AmazonS3Exception ex) => ex.ErrorCode switch
    {
        "InvalidAccessKeyId" or "SignatureDoesNotMatch" =>
            new DocStorageException($"The file store at {_host} did not accept the storage keys. Check Storage__Minio__AccessKey and Storage__Minio__SecretKey.", ex),
        "AccessDenied" =>
            new DocStorageException($"The storage keys may not write to the bucket '{_bucket}' on {_host}. Give them write access to it.", ex),
        "NoSuchBucket" =>
            new DocStorageException($"The bucket '{_bucket}' does not exist on {_host}.", ex),
        _ => new DocStorageException($"The file store at {_host} refused the file ({ex.ErrorCode ?? ((int)ex.StatusCode).ToString()}).", ex),
    };

    public async Task<Stream> OpenAsync(string key)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, _folder + key);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorCode != "NoSuchBucket")
        {
            throw new FileNotFoundException("The file is not in storage.", key);
        }
        catch (AmazonS3Exception ex)
        {
            throw Explain(ex);
        }
        catch (Exception ex) when (ex is AmazonClientException or HttpRequestException)
        {
            throw new DocStorageException($"The file store at {_host} cannot be reached. Try again in a minute.", ex);
        }
    }
}

/// <summary>
/// Used when no storage is set up. Written pages, folders and links all work; uploading or opening
/// a file is refused with a clear message (DocsService checks IsConfigured before calling these).
/// </summary>
public class MissingDocFileStore : IDocFileStore
{
    public bool IsConfigured => false;

    public Task SaveAsync(string key, Stream content, string contentType) =>
        throw new InvalidOperationException("File storage is not set up.");

    public Task<Stream> OpenAsync(string key) =>
        throw new InvalidOperationException("File storage is not set up.");
}
