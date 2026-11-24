namespace CMSAPI.Domain.Entities;

/// <summary>A CMS person's profile photo (one per person; the CMS sends it resized to 512 px).</summary>
public class CmsUserPhoto
{
    public Guid UserId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime UpdatedAt { get; set; }
}
