using System;

namespace CMSAPI.Application.Models
{
    public class ReferralCodeTypeDto
    {
        public Guid ReferralCodeTypeId { get; set; }
        public string Name { get; set; } = null!;
        public string RewardKind { get; set; } = null!;
        public decimal RewardValue { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateReferralCodeTypeRequest
    {
        public string Name { get; set; } = null!;
        public string RewardKind { get; set; } = null!;
        public decimal RewardValue { get; set; }
    }

    public class UpdateReferralCodeTypeRequest
    {
        public string Name { get; set; } = null!;
        public string RewardKind { get; set; } = null!;
        public decimal RewardValue { get; set; }
        public bool IsActive { get; set; }
    }

    public class ReferralCodeDto
    {
        public Guid ReferralCodeId { get; set; }
        public Guid ReferralCodeTypeId { get; set; }
        public string ReferralCodeTypeName { get; set; } = null!;
        public string RewardKind { get; set; } = null!;
        public decimal RewardValue { get; set; }
        public string Code { get; set; } = null!;
        public bool IsActive { get; set; }
        public Guid? RedeemedByHospitalId { get; set; }
        public DateTime? RedeemedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateReferralCodeRequest
    {
        public Guid ReferralCodeTypeId { get; set; }
        // Blank/null = auto-generate a unique code server-side.
        public string? Code { get; set; }
    }

    public class ValidateReferralCodeResponse
    {
        public bool Valid { get; set; }
        public string? Message { get; set; }
        public string? RewardKind { get; set; }
        public decimal? RewardValue { get; set; }
        public string? ReferralCodeTypeName { get; set; }
    }
}
