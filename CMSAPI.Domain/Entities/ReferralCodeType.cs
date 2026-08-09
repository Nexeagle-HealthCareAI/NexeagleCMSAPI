using System;

namespace CMSAPI.Domain.Entities
{
    public static class ReferralRewardKind
    {
        public const string PercentageOff = "PercentageOff";
        public const string ExtraMonths = "ExtraMonths";
    }

    public class ReferralCodeType
    {
        public Guid ReferralCodeTypeId { get; set; }
        public string Name { get; set; } = null!;
        public string RewardKind { get; set; } = null!; // ReferralRewardKind.PercentageOff | ExtraMonths
        public decimal RewardValue { get; set; }
        public bool IsActive { get; set; } = true;

        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
