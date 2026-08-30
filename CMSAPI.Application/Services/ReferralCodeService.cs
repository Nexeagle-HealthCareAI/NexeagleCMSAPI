using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Services
{
    public class ReferralCodeService : IReferralCodeService
    {
        private readonly IReferralCodeRepository _repo;

        public ReferralCodeService(IReferralCodeRepository repo)
        {
            _repo = repo;
        }

        public async Task<IEnumerable<ReferralCodeTypeDto>> GetAllTypesAsync()
        {
            var types = await _repo.GetAllTypesAsync();
            return types.Select(MapType);
        }

        public async Task<ReferralCodeTypeDto> CreateTypeAsync(CreateReferralCodeTypeRequest request, Guid? createdByUserId)
        {
            var type = new ReferralCodeType
            {
                ReferralCodeTypeId = Guid.NewGuid(),
                Name = request.Name.Trim(),
                RewardKind = request.RewardKind,
                RewardValue = request.RewardValue,
                IsActive = true,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            await _repo.CreateTypeAsync(type);
            return MapType(type);
        }

        public async Task<ReferralCodeTypeDto?> UpdateTypeAsync(Guid referralCodeTypeId, UpdateReferralCodeTypeRequest request)
        {
            var type = await _repo.GetTypeByIdAsync(referralCodeTypeId);
            if (type == null) return null;

            type.Name = request.Name.Trim();
            type.RewardKind = request.RewardKind;
            type.RewardValue = request.RewardValue;
            type.IsActive = request.IsActive;
            await _repo.UpdateTypeAsync(type);
            return MapType(type);
        }

        public async Task<IEnumerable<ReferralCodeDto>> GetAllCodesAsync()
        {
            var codes = await _repo.GetAllCodesAsync();
            return codes.Select(MapCode);
        }

        public async Task<ReferralCodeDto> CreateCodeAsync(CreateReferralCodeRequest request, Guid? createdByUserId)
        {
            var type = await _repo.GetTypeByIdAsync(request.ReferralCodeTypeId)
                ?? throw new InvalidOperationException("Referral code type not found.");

            string code;
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                code = await GenerateUniqueCodeAsync();
            }
            else
            {
                code = request.Code.Trim().ToUpperInvariant();
                if (await _repo.ExistsCodeAsync(code))
                    throw new InvalidOperationException($"Code '{code}' is already in use.");
            }

            var entity = new ReferralCode
            {
                ReferralCodeId = Guid.NewGuid(),
                ReferralCodeTypeId = request.ReferralCodeTypeId,
                Code = code,
                IsActive = true,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };
            await _repo.CreateCodeAsync(entity);
            entity.ReferralCodeType = type;
            return MapCode(entity);
        }

        public async Task<ReferralCodeDto?> SetCodeActiveAsync(Guid referralCodeId, bool isActive)
        {
            var code = await _repo.GetCodeByIdAsync(referralCodeId);
            if (code == null) return null;

            code.IsActive = isActive;
            await _repo.UpdateCodeAsync(code);

            var type = await _repo.GetTypeByIdAsync(code.ReferralCodeTypeId);
            code.ReferralCodeType = type;
            return MapCode(code);
        }

        public async Task<ValidateReferralCodeResponse> ValidateAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new ValidateReferralCodeResponse { Valid = false, Message = "No code provided." };

            var entity = await _repo.GetByCodeAsync(code);
            if (entity == null)
                return new ValidateReferralCodeResponse { Valid = false, Message = "Referral code not found." };

            if (!entity.IsActive || entity.ReferralCodeType == null || !entity.ReferralCodeType.IsActive)
                return new ValidateReferralCodeResponse { Valid = false, Message = "Referral code is no longer active." };

            if (entity.RedeemedByHospitalId != null)
                return new ValidateReferralCodeResponse { Valid = false, Message = "Referral code has already been used." };

            return new ValidateReferralCodeResponse
            {
                Valid = true,
                RewardKind = entity.ReferralCodeType.RewardKind,
                RewardValue = entity.ReferralCodeType.RewardValue,
                ReferralCodeTypeName = entity.ReferralCodeType.Name
            };
        }

        // Mirrors CmsPartnerService.GeneratePartnerCode()'s charset/length, but with a collision
        // retry loop -- a referral code collision would double-grant a reward, unlike a partner
        // code collision which has no financial effect.
        private async Task<string> GenerateUniqueCodeAsync()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var candidate = GenerateCode();
                if (!await _repo.ExistsCodeAsync(candidate))
                    return candidate;
            }
            throw new InvalidOperationException("Could not generate a unique referral code. Please try again.");
        }

        private static string GenerateCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            using var rng = RandomNumberGenerator.Create();
            var result = new char[6];
            var buffer = new byte[6];
            rng.GetBytes(buffer);
            for (int i = 0; i < 6; i++)
            {
                result[i] = chars[buffer[i] % chars.Length];
            }
            return new string(result);
        }

        private static ReferralCodeTypeDto MapType(ReferralCodeType type) => new()
        {
            ReferralCodeTypeId = type.ReferralCodeTypeId,
            Name = type.Name,
            RewardKind = type.RewardKind,
            RewardValue = type.RewardValue,
            IsActive = type.IsActive,
            CreatedAt = type.CreatedAt
        };

        private static ReferralCodeDto MapCode(ReferralCode code) => new()
        {
            ReferralCodeId = code.ReferralCodeId,
            ReferralCodeTypeId = code.ReferralCodeTypeId,
            ReferralCodeTypeName = code.ReferralCodeType?.Name ?? "Unknown",
            RewardKind = code.ReferralCodeType?.RewardKind ?? "",
            RewardValue = code.ReferralCodeType?.RewardValue ?? 0,
            Code = code.Code,
            IsActive = code.IsActive,
            RedeemedByHospitalId = code.RedeemedByHospitalId,
            RedeemedAt = code.RedeemedAt,
            CreatedAt = code.CreatedAt
        };
    }
}
