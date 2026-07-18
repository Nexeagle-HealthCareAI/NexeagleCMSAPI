using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Domain.Entities
{
    public class HospitalSubscription
    {
        [Key]
        public Guid HospitalSubscriptionId { get; set; }
        
        public Guid HospitalId { get; set; }
        public Hospital? Hospital { get; set; }
        
        public Guid? PlanId { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Trial"; // Trial, Active, Expired, Blocked
        
        public DateTime? TrialStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public DateTime? SubscriptionStartDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public DateTime? NextBillingDate { get; set; }
        
        // Denormalized copy of the selected plan's limits, populated when the subscription is
        // approved (see SubscriptionApprovalController.ApprovePayment) -- lets easyHMSAPI enforce
        // doctor/bed limits with a single local lookup instead of calling CMSAPI on every
        // doctor/bed creation. NULL = unlimited (Trial, or an Enterprise plan).
        public int? MaxDoctors { get; set; }
        public int? MaxBeds { get; set; }

        public decimal? PaymentAmount { get; set; }
        
        [MaxLength(100)]
        public string? PaymentReference { get; set; }

        public DateTime? PaymentDate { get; set; }

        [MaxLength(50)]
        public string? PaymentMode { get; set; }

        // Set when a CMS admin rejects a submitted payment (Status becomes "Rejected"); surfaced
        // back to the hospital admin on the EasyHMS subscription page.
        [MaxLength(500)]
        public string? RejectionReason { get; set; }
        public DateTime? RejectedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Mirrors easyHMSAPI's HospitalSubscription.GetEffectiveStatus — nothing flips Status to
        // "Expired" in the background, so callers (e.g. the Onboarded Hospitals list) must compute
        // this instead of trusting the raw column.
        public string GetEffectiveStatus(DateTime utcNow)
        {
            if (Status.Equals("Trial", StringComparison.OrdinalIgnoreCase) && TrialEndDate.HasValue && TrialEndDate.Value <= utcNow)
                return "Expired";
            if (Status.Equals("Active", StringComparison.OrdinalIgnoreCase) && SubscriptionEndDate.HasValue && SubscriptionEndDate.Value <= utcNow)
                return "Expired";
            return Status;
        }
    }
}
