namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Where uploaded files are kept. The database holds only a file's details and its key here.
/// In production this is the MinIO object store the other NexEagle APIs use.
/// </summary>
public interface IDocFileStore
{
    /// <summary>False when no storage is set up: pages still work, uploads and downloads are refused.</summary>
    bool IsConfigured { get; }
    Task SaveAsync(string key, Stream content, string contentType);
    Task<Stream> OpenAsync(string key);
}

/// <summary>
/// The file store could not take or give a file: wrong keys, a missing bucket, or the store cannot be
/// reached. The message says which, in words an administrator can act on; nothing secret is in it.
/// </summary>
public class DocStorageException : Exception
{
    public DocStorageException(string message, Exception? inner = null) : base(message, inner) { }
}
