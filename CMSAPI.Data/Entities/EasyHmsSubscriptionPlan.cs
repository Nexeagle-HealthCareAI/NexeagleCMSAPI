using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Data.Entities
{
    // Dedicated EasyHMS plan catalog -- see dbo.SubscriptionPlan for why this isn't just more
    // columns on the shared (1Rad) SubscriptionPlans table.
    public class EasyHmsSubscriptionPlan
    {
        [Key]
        public Guid PlanId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BasePrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPrice { get; set; }

        [Required]
        [MaxLength(50)]
        public string BillingCycle { get; set; } = null!; // Monthly or Yearly

        public bool IsActive { get; set; } = true;

        // JSON array of feature strings shown on the plan tile, e.g. ["OPD","IPD","Billing"].
        public string? Features { get; set; }

        // NULL = unlimited (used for the Enterprise tier; enforced server-side in easyHMSAPI).
        public int? MaxDoctors { get; set; }
        public int? MaxBeds { get; set; }

        // Enterprise tiers have no fixed price -- the EasyHMS tile renders a "Contact Us" CTA
        // instead of BasePrice/DiscountPrice/a Select-Plan button when this is true.
        public bool IsEnterprise { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
