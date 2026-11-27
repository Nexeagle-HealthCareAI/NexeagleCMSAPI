using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Data.Entities
{
    /// <summary>
    /// Records every CMS-initiated change to a hospital's subscription. Lives in
    /// CMSDatabase so history for both product platforms sits in one place,
    /// independent of which product database the change was applied to.
    /// </summary>
    public class SubscriptionAuditLog
    {
        [Key]
        public Guid AuditId { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        /// <summary>CMS user who performed the action (null if it could not be resolved).</summary>
        public Guid? ActorUserId { get; set; }

        [MaxLength(256)]
        public string? ActorEmail { get; set; }

        /// <summary>Product platform the change targeted: "EasyHMS" or "1Rad".</summary>
        [Required]
        [MaxLength(50)]
        public string Platform { get; set; } = null!;

        public Guid HospitalId { get; set; }

        /// <summary>Action performed: "status", "trial", "validity", or "plan".</summary>
        [Required]
        [MaxLength(50)]
        public string Action { get; set; } = null!;

        [MaxLength(1000)]
        public string? OldValue { get; set; }

        [MaxLength(1000)]
        public string? NewValue { get; set; }
    }
}
