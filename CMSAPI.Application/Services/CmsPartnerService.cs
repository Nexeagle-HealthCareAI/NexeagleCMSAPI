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
    public class CmsPartnerService : ICmsPartnerService
    {
        private readonly ICmsPartnerRepository _partnerRepo;

        public CmsPartnerService(ICmsPartnerRepository partnerRepo)
        {
            _partnerRepo = partnerRepo;
        }

        public async Task<IEnumerable<PartnerDto>> GetAllPartnersAsync()
        {
            var partners = await _partnerRepo.GetAllPartnersAsync();
            return partners.Select(MapToDto);
        }

        public async Task<PartnerDto> CreatePartnerAsync(CreatePartnerRequest request, Guid? createdByUserId)
        {
            var partner = new CmsPartner
            {
                PartnerId = Guid.NewGuid(),
                Name = request.Name,
                Age = request.Age,
                Sex = request.Sex,
                HighestQualification = request.HighestQualification,
                CurrentProfession = request.CurrentProfession,
                Address = request.Address,
                City = request.City,
                State = request.State,
                Country = request.Country,
                Pincode = request.Pincode,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                PartnerCode = GeneratePartnerCode(),
                DashboardToken = GenerateSecureToken(),
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTime.UtcNow
            };

            await _partnerRepo.CreatePartnerAsync(partner);
            return MapToDto(partner);
        }

        public async Task<bool> DeletePartnerAsync(Guid partnerId)
        {
            return await _partnerRepo.DeletePartnerAsync(partnerId);
        }

        public async Task<PartnerDashboardDto?> GetDashboardStatsByTokenAsync(string token)
        {
            var partner = await _partnerRepo.GetPartnerByTokenAsync(token);
            if (partner == null) return null;

            // In the future, we would query HospitalRepository for hospitals onboarded by this partner
            int totalOnboarded = 0; 

            return new PartnerDashboardDto
            {
                Profile = MapToDto(partner),
                TotalHospitalsOnboarded = totalOnboarded
            };
        }

        private static PartnerDto MapToDto(CmsPartner entity)
        {
            return new PartnerDto
            {
                PartnerId = entity.PartnerId,
                Name = entity.Name,
                Age = entity.Age,
                Sex = entity.Sex,
                HighestQualification = entity.HighestQualification,
                CurrentProfession = entity.CurrentProfession,
                Address = entity.Address,
                City = entity.City,
                State = entity.State,
                Country = entity.Country,
                Pincode = entity.Pincode,
                Email = entity.Email,
                PhoneNumber = entity.PhoneNumber,
                PartnerCode = entity.PartnerCode,
                DashboardToken = entity.DashboardToken,
                CreatedAt = entity.CreatedAt
            };
        }

        private static string GeneratePartnerCode()
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

        private static string GenerateSecureToken()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", ""); // Url-safe Base64
        }
    }
}
