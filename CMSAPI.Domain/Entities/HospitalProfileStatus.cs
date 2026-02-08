using System;

namespace CMSAPI.Domain.Entities;

public class HospitalProfileStatus
{
    public Guid HospitalID { get; set; }
    public bool IsBasicInfoComplete { get; set; }
    public bool IsContactInfoComplete { get; set; }
    public bool IsLocationInfoComplete { get; set; }
    public int ProfileCompletionPercent { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    // Navigation
    public Hospital? Hospital { get; set; }
}
