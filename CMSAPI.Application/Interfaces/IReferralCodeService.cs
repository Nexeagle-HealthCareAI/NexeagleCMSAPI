using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces
{
    public interface IReferralCodeService
    {
        Task<IEnumerable<ReferralCodeTypeDto>> GetAllTypesAsync();
        Task<ReferralCodeTypeDto> CreateTypeAsync(CreateReferralCodeTypeRequest request, Guid? createdByUserId);
        Task<ReferralCodeTypeDto?> UpdateTypeAsync(Guid referralCodeTypeId, UpdateReferralCodeTypeRequest request);

        Task<IEnumerable<ReferralCodeDto>> GetAllCodesAsync();
        Task<ReferralCodeDto> CreateCodeAsync(CreateReferralCodeRequest request, Guid? createdByUserId);
        Task<ReferralCodeDto?> SetCodeActiveAsync(Guid referralCodeId, bool isActive);

        // Used by both the CMS-authenticated preview and the easyHMSAPI service-to-service call.
        // Read-only -- does not reserve/lock the code (see plan's documented redemption-race limitation).
        Task<ValidateReferralCodeResponse> ValidateAsync(string code);
    }
}
