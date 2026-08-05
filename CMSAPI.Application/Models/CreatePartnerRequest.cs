using System.ComponentModel.DataAnnotations;

namespace CMSAPI.Application.Models
{
    public class CreatePartnerRequest
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;
        
        [Required, Range(18, 120)]
        public int Age { get; set; }

        // Matches AddPartnerModal.tsx's <select> exactly -- the client only ever submits one of
        // these, but the server has to enforce it too (client-side validation alone can't stop a
        // direct API call).
        [Required, MaxLength(20)]
        [RegularExpression("^(Male|Female|Other)$", ErrorMessage = "Sex must be Male, Female, or Other.")]
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
