using System;

namespace CMSAPI.Application.Models
{
    public class PartnerDto
    {
        public Guid PartnerId { get; set; }
        public string Name { get; set; } = null!;
        public int Age { get; set; }
        public string Sex { get; set; } = null!;
        public string HighestQualification { get; set; } = null!;
        public string CurrentProfession { get; set; } = null!;
        public string Address { get; set; } = null!;
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Country { get; set; } = null!;
        public string Pincode { get; set; } = null!;
        
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        
        public string PartnerCode { get; set; } = null!;
        
        // Return this token only when creating or when explicitly fetching for sharing
        public string DashboardToken { get; set; } = null!;
        
        public DateTime CreatedAt { get; set; }
    }
}
