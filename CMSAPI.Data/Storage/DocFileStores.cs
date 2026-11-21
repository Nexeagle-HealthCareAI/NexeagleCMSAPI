using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using CMSAPI.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data.Storage;

/// <summary>
/// Keeps uploaded documents in the self-hosted, S3-compatible MinIO store that 1RADAPI uses
/// (see its MinioBlobService), in a bucket of the CMS's own.
///
/// Settings (environment names in brackets). The first three are the same ones 1RADAPI reads:
///   Storage:PublicBaseUrl      [Storage__PublicBaseUrl]      e.g. http://vm-host:9000
///   Storage:Minio:AccessKey    [Storage__Minio__AccessKey]
///   Storage:Minio:SecretKey    [Storage__Minio__SecretKey]
///   Storage:Minio:DocsBucket   [Storage__Minio__DocsBucket]  default "cms-docs"; must already exist
///   Storage:Minio:Region       [Storage__Minio__Region]      default "us-east-1"
///
/// The bucket is private. Files are only ever read by this API, after it has checked who is asking,
/// so no link to the store is ever handed to a browser.
/// </summary>
public class MinioDocFileStore : IDocFileStore
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public MinioDocFileStore(IConfiguration configuration)
    {
        var endpoint = configuration["Storage:PublicBaseUrl"] ?? configuration["Storage:Minio:PublicBaseUrl"];
        var accessKey = configuration["Storage:Minio:AccessKey"];
        var secretKey = configuration["Storage:Minio:SecretKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("MINIO_CONFIG_MISSING: set Storage:PublicBaseUrl, Storage:Minio:AccessKey and Storage:Minio:SecretKey.");

        var bucket = configuration["Storage:Minio:DocsBucket"];
        _bucket = string.IsNullOrWhiteSpace(bucket) ? "cms-docs" : bucket;

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint.TrimEnd('/'),   // the scheme decides http or https
            ForcePathStyle = true,                // MinIO needs path-style addresses
            AuthenticationRegion = configuration["Storage:Minio:Region"] ?? "us-east-1",
        };
        _s3 = new AmazonS3Client(accessKey, secretKey, config);
    }

    public bool IsConfigured => true;

    public async Task SaveAsync(string key, Stream content, string contentType)
    {
        // PutObject needs to know the length, so a stream that cannot seek is copied to memory first.
        var stream = content;
        MemoryStream? buffer = null;
        if (!content.CanSeek)
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            buffer.Position = 0;
            stream = buffer;
        }

        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
                AutoCloseStream = false,   // the caller owns the stream it gave us
            });
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public async Task<Stream> OpenAsync(string key)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, key);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("The file is not in storage.", key);
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
