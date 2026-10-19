using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// The NLP router's 32-class taxonomy (see 1HMS-NLP-Router's specialty_mapping.py) — kept in
// sync manually since the two repos don't share code. Used to validate Specialist on
// insert/update so CMS can't accidentally introduce a label the model would never predict
// or map anywhere.
public static class SymptomRouterConstants
{
    public static readonly HashSet<string> ValidSpecialists = new()
    {
        "General Physician", "Paediatrician", "Cardiologist (Heart)", "Dermatologist (Skin)",
        "Orthopaedic Surgeon (Bone)", "Gynaecologist", "Dentist", "ENT Specialist",
        "Ophthalmologist (Eye)", "Neurologist", "Psychiatrist", "Urologist", "Gastroenterologist",
        "Endocrinologist (Hormones/Diabetes)", "Pulmonologist (Chest/Lungs)", "Nephrologist (Kidney)",
        "Oncologist (Cancer)", "Rheumatologist", "Physiotherapist / Rehab", "General Surgeon",
        "Neurosurgeon", "Plastic Surgeon", "Vascular Surgeon", "Cardiothoracic Surgeon",
        "Anaesthesiologist", "Radiologist", "Pathologist", "Emergency Medicine Specialist",
        "Geriatrician", "Sports Medicine Specialist", "GI/Surgical Gastroenterologist", "Veterinarian",
    };

    // Router specialtyId (1HMS-NLP-Router hms_router/domain/taxonomy.py) -> the training-table label above
    // (inverse of the router's LEGACY_LABEL_TO_ID). Used when an admin approves a review-queue item.
    public static readonly IReadOnlyDictionary<string, string> SpecialtyIdToLabel = new Dictionary<string, string>
    {
        ["general"] = "General Physician", ["pediatrics"] = "Paediatrician", ["cardiology"] = "Cardiologist (Heart)",
        ["dermatology"] = "Dermatologist (Skin)", ["orthopedics"] = "Orthopaedic Surgeon (Bone)", ["gynecology"] = "Gynaecologist",
        ["dentistry"] = "Dentist", ["ent"] = "ENT Specialist", ["ophthalmology"] = "Ophthalmologist (Eye)",
        ["neurology"] = "Neurologist", ["psychiatry"] = "Psychiatrist", ["urology"] = "Urologist",
        ["gastroenterology"] = "Gastroenterologist", ["endocrinology"] = "Endocrinologist (Hormones/Diabetes)",
        ["pulmonology"] = "Pulmonologist (Chest/Lungs)", ["nephrology"] = "Nephrologist (Kidney)",
        ["oncology"] = "Oncologist (Cancer)", ["rheumatology"] = "Rheumatologist", ["physiotherapy"] = "Physiotherapist / Rehab",
        ["generalsurgery"] = "General Surgeon", ["neurosurgery"] = "Neurosurgeon", ["plasticsurgery"] = "Plastic Surgeon",
        ["vascularsurgery"] = "Vascular Surgeon", ["cardiothoracicsurgery"] = "Cardiothoracic Surgeon",
        ["anesthesiology"] = "Anaesthesiologist", ["radiology"] = "Radiologist", ["pathology"] = "Pathologist",
        ["emergencymedicine"] = "Emergency Medicine Specialist", ["geriatrics"] = "Geriatrician",
        ["sportsmedicine"] = "Sports Medicine Specialist",
    };

    /// <summary>Training-table label (or a close variant the chat agent uses) -> router specialtyId; null if unknown.</summary>
    public static string? LabelToSpecialtyId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var l = label.Trim();
        foreach (var kv in SpecialtyIdToLabel)
            if (string.Equals(kv.Value, l, StringComparison.OrdinalIgnoreCase) || string.Equals(kv.Key, l, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return null;
    }
}
