using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces
{
    public interface ICmsPartnerRepository
    {
        Task<IEnumerable<CmsPartner>> GetAllPartnersAsync();
        Task<CmsPartner?> GetPartnerByIdAsync(Guid partnerId);
        Task<CmsPartner?> GetPartnerByTokenAsync(string token);
        Task<CmsPartner> CreatePartnerAsync(CmsPartner partner);
    }
}
