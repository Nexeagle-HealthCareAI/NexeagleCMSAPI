using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Data.Entities
{
    public class SubscriptionPlan
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
        
        [Required]
        [MaxLength(50)]
        public string ApplicationName { get; set; } = "1Rad"; // "1Rad" or "EasyHMS"
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
