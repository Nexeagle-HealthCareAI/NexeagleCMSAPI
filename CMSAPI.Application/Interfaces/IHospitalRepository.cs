using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Interfaces;

public interface IHospitalRepository
{
    Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir, string? status = null, string? subscriptionStatus = null, bool includeArchived = false);
    Task<HospitalDetails?> GetHospitalByIdAsync(Guid id);
    Task<HospitalAppointmentSourceStats> GetAppointmentSourceStatsAsync(Guid hospitalId, DateOnly? from, DateOnly? to);
    Task<bool> ArchiveHospitalAsync(Guid id, Guid archivedByUserId);
    Task<bool> RestoreHospitalAsync(Guid id);

    // Feeds the Data Migration transform step's patient-identity crosswalk -- a lightweight
    // snapshot of this hospital's existing patients, read-only, never used for writes here.
    Task<System.Collections.Generic.List<PatientIdentitySnapshot>> GetPatientIdentitySnapshotAsync(Guid hospitalId);
}
