using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces
{
    public interface IReferralCodeRepository
    {
        Task<IEnumerable<ReferralCodeType>> GetAllTypesAsync();
        Task<ReferralCodeType?> GetTypeByIdAsync(Guid referralCodeTypeId);
        Task<ReferralCodeType> CreateTypeAsync(ReferralCodeType type);
        Task<ReferralCodeType> UpdateTypeAsync(ReferralCodeType type);

        Task<IEnumerable<ReferralCode>> GetAllCodesAsync();
        Task<ReferralCode?> GetCodeByIdAsync(Guid referralCodeId);
        Task<ReferralCode?> GetByCodeAsync(string code);
        Task<bool> ExistsCodeAsync(string code);
        Task<ReferralCode> CreateCodeAsync(ReferralCode code);
        Task<ReferralCode> UpdateCodeAsync(ReferralCode code);
    }
}
