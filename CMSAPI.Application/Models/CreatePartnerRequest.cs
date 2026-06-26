using System.ComponentModel.DataAnnotations;

namespace CMSAPI.Application.Models
{
    public class CreatePartnerRequest
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;
        
        [Required]
        public int Age { get; set; }
        
        [Required, MaxLength(20)]
        public string Sex { get; set; } = null!;
        
        [Required, MaxLength(100)]
        public string HighestQualification { get; set; } = null!;
        
        [Required, MaxLength(100)]
        public string CurrentProfession { get; set; } = null!;
        
        [Required, MaxLength(255)]
        public string Address { get; set; } = null!;
        
        [Required, MaxLength(100)]
        public string City { get; set; } = null!;
        
        [Required, MaxLength(100)]
        public string State { get; set; } = null!;
        
        [Required, MaxLength(100)]
        public string Country { get; set; } = null!;
        
        [Required, MaxLength(20)]
        public string Pincode { get; set; } = null!;
        
        [EmailAddress, MaxLength(256)]
        public string? Email { get; set; }
        
        [MaxLength(20)]
        public string? PhoneNumber { get; set; }
    }
}
