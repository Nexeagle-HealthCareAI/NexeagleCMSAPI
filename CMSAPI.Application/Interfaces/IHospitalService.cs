using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Interfaces;

public interface IHospitalService
{
    Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir, string? status = null, string? subscriptionStatus = null, bool includeArchived = false);
    Task<HospitalDetails?> GetHospitalByIdAsync(Guid id);
    Task<HospitalAppointmentSourceStats> GetAppointmentSourceStatsAsync(Guid hospitalId, DateOnly? from, DateOnly? to);
    Task<bool> ArchiveHospitalAsync(Guid id, Guid archivedByUserId);
    Task<bool> RestoreHospitalAsync(Guid id);
    Task<System.Collections.Generic.List<PatientIdentitySnapshot>> GetPatientIdentitySnapshotAsync(Guid hospitalId);
}
