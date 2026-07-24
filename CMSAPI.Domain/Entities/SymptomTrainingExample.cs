using System;

namespace CMSAPI.Domain.Entities;

// The CMS-editable training set for the Hinglish symptom -> specialist NLP router (see
// 1HMS-NLP-Router repo). Same table CMSAPI reads/writes; the retrain pipeline pulls whatever
// is IsActive=1 here (plus correlated production feedback from AnalyticsEvents) each run.
public class SymptomTrainingExample
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    // One of the 32 canonical patient-facing specialist labels the router outputs — validated
    // against SymptomRouterConstants.ValidSpecialists before insert/update (see service layer),
    // not a DB foreign key, since the taxonomy lives in the NLP repo's code, not this DB.
    public string Specialist { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string Source { get; set; } = "Seed";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
