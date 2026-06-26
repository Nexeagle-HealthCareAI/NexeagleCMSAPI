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
        
        public decimal? PaymentAmount { get; set; }
        
        [MaxLength(100)]
        public string? PaymentReference { get; set; }
        
        public DateTime? PaymentDate { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
