using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

public class DoctorListItem
{
    public Guid DoctorId { get; set; }
    public string? FullName { get; set; }
    public Guid HospitalId { get; set; }
    public string? HospitalName { get; set; }
    // Hospital.Location + City + State + Pincode joined — same convention easyHMSWeb's
    // opdDocuments.ts/TokenPrintModal.tsx use for a display-ready hospital address.
    public string? HospitalAddress { get; set; }
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
    // From UserAuth.LastLoginTime (UTC) — null if the doctor has never logged in.
    public DateTime? LastLoginTime { get; set; }
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

// Campaign-style bulk action — applies the SAME change to many doctors in one save. Unlike
// UpdateDoctorMarketingRequest's full-replace semantics, each field here is opt-in: null/false
// "don't touch" flags mean that field is left exactly as-is for every selected doctor, so an admin
// can e.g. bulk-delist without silently clearing everyone's existing discount.
public class BulkUpdateDoctorMarketingRequest
{
    public List<Guid> DoctorIds { get; set; } = new();

    // null = leave IsFeatured untouched for all selected doctors.
    public bool? IsFeatured { get; set; }
    // null = leave IsDelistedByAdmin untouched for all selected doctors.
    public bool? IsDelistedByAdmin { get; set; }

    // When false, DiscountPercent/DiscountStartAt/DiscountEndAt below are ignored entirely and no
    // selected doctor's discount is touched. When true, all three are applied as a full replace
    // (DiscountPercent = null clears the discount for every selected doctor).
    public bool UpdateDiscount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public DateTime? DiscountStartAt { get; set; }
    public DateTime? DiscountEndAt { get; set; }
}

public class BulkUpdateDoctorMarketingResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int UpdatedCount { get; set; }
    public List<Guid> NotFoundDoctorIds { get; set; } = new();
}
