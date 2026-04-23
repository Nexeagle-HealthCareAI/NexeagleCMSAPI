using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Domain.Entities;

public class SupportMessage
{
    [Key]
    public Guid MessageId { get; set; }

    [Required]
    public Guid SessionId { get; set; }

    [Required]
    [MaxLength(20)]
    public string SenderType { get; set; } = string.Empty; // 'Guest', 'Agent'

    [MaxLength(100)]
    public string? SenderId { get; set; }

    [Required]
    public string MessageText { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("SessionId")]
    public SupportSession Session { get; set; } = null!;
}
