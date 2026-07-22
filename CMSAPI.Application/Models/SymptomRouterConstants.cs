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
}
