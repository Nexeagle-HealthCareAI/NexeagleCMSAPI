using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IRadHospitalsService
{
    Task<List<RadHospitalItem>> GetAllAsync();
}
