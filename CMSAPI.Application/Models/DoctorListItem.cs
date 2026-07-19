using System;

namespace CMSAPI.Application.Models;

public class DoctorListItem
{
    public Guid DoctorId { get; set; }
    public string? FullName { get; set; }
    public Guid HospitalId { get; set; }
    public string? HospitalName { get; set; }
    public string? DepartmentName { get; set; }
    // Current OPD_CONSULT DoctorFee.Amount, for context when setting a discount — null when no
    // active fee is configured at this doctor's hospital.
    public decimal? OpdConsultFee { get; set; }
    public bool IsPubliclyListed { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsDelistedByAdmin { get; set; }
    public decimal? DiscountPercent { get; set; }
    public DateTime? DiscountStartAt { get; set; }
    public DateTime? DiscountEndAt { get; set; }
}

// Full-replace write model — whatever this says is exactly what gets set (including explicit
// nulls, e.g. to clear a discount), rather than partial-PATCH "null means skip" semantics, which
// has no way to express "clear this field."
public class UpdateDoctorMarketingRequest
{
    public bool IsFeatured { get; set; }
    public bool IsDelistedByAdmin { get; set; }
    public decimal? DiscountPercent { get; set; }
    public DateTime? DiscountStartAt { get; set; }
    public DateTime? DiscountEndAt { get; set; }
}

public class UpdateDoctorMarketingResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
