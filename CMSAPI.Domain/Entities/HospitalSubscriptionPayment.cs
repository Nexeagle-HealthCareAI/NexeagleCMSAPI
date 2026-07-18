using System;
using System.ComponentModel.DataAnnotations;

namespace CMSAPI.Domain.Entities
{
    // Mirrors easyHMSAPI's entity of the same name — both map to the same physical
    // HospitalSubscriptionPayments table in easyHMSDatabase (CMSAPI's AppDbContext connects
    // there directly). Approve/Reject update the matching row here so the hospital's payment
    // history reflects the outcome.
    public class HospitalSubscriptionPayment
    {
        [Key]
        public Guid PaymentId { get; set; }

        public Guid HospitalId { get; set; }
        public Guid HospitalSubscriptionId { get; set; }

        public Guid? PlanId { get; set; }
        [MaxLength(200)]
        public string? PlanName { get; set; }

        public decimal Amount { get; set; }

        [Required]
        [MaxLength(100)]
        public string Reference { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? PaymentMode { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "PendingApproval"; // PendingApproval, Approved, Rejected

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }

        [MaxLength(500)]
        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
