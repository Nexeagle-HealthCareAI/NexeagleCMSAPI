using System.ComponentModel.DataAnnotations;

namespace CMSAPI.Domain.Entities;

public class SupportSession
{
    [Key]
    public Guid SessionId { get; set; }

    [Required]
    [MaxLength(100)]
    public string GuestId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? GuestName { get; set; }

    [MaxLength(150)]
    public string? GuestEmail { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    public ICollection<SupportMessage> Messages { get; set; } = new List<SupportMessage>();
}
