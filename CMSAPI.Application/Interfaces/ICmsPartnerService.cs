using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces
{
    public interface ICmsPartnerService
    {
        Task<IEnumerable<PartnerDto>> GetAllPartnersAsync();
        Task<PartnerDto> CreatePartnerAsync(CreatePartnerRequest request, Guid? createdByUserId);
        Task<bool> DeletePartnerAsync(Guid partnerId);
        Task<PartnerDashboardDto?> GetDashboardStatsByTokenAsync(string token);
    }
    
    public class PartnerDashboardDto
    {
        public PartnerDto Profile { get; set; } = null!;
        // Later we can add stats here (e.g. onboarded hospitals)
        public int TotalHospitalsOnboarded { get; set; }
    }
}
