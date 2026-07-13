using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Data.Entities
{
    /// <summary>
    /// Admin-configured add-on price for a feature module on a platform.
    /// <c>Charge = 0</c> means the module is free / included.
    /// </summary>
    public class ModuleCharge
    {
        [Key]
        public Guid ModuleChargeId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ApplicationName { get; set; } = null!;  // EasyHMS | 1Rad

        [Required]
        [MaxLength(20)]
        public string ModuleKey { get; set; } = null!;         // IPD | OPD | Billing | Lab | RAD

        [Column(TypeName = "decimal(18,2)")]
        public decimal Charge { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
