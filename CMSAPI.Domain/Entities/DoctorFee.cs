using System;

namespace CMSAPI.Domain.Entities;

// Mirrors easyHMSAPI's DoctorFee (dbo.DoctorFee) — read-only from CMSAPI's side, used only to show
// the CMS admin the current OPD consult fee for context when configuring a discount.
public class DoctorFee
{
    public Guid DoctorFeeId { get; set; }
    public Guid HospitalId { get; set; }
    public Guid DoctorId { get; set; }
    public string FeeType { get; set; } = string.Empty; // OPD_CONSULT / IPD_VISIT
    public decimal Amount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
