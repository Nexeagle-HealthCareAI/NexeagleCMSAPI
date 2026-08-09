using System;

namespace CMSAPI.Domain.Entities
{
    public class ReferralCode
    {
        public Guid ReferralCodeId { get; set; }
        public Guid ReferralCodeTypeId { get; set; }
        public ReferralCodeType? ReferralCodeType { get; set; }

        public string Code { get; set; } = null!;
        public bool IsActive { get; set; } = true;

        // No FK -- Hospital lives in easyHMSDatabase, a different physical database.
        public Guid? RedeemedByHospitalId { get; set; }
        public DateTime? RedeemedAt { get; set; }

        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
