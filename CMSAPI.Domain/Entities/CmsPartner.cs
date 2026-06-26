using System;

namespace CMSAPI.Domain.Entities
{
    public class CmsPartner
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
        
        public string DashboardToken { get; set; } = null!;
        
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
