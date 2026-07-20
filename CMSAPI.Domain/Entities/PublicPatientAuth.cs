using System;

namespace CMSAPI.Domain.Entities;

// WhatsApp-OTP login identity for the public "Doctor Dekho" booking portal (same table
// easyHMSAPI's PatientOtpVerifyHandler writes to). Only the fields the CMS "Patient Logins"
// report needs are mapped here — OTP/security-internal columns (Otp, FailedAttempts, IsLocked,
// SessionEpoch, etc.) deliberately aren't, since CMSAPI only ever reads this table.
public class PublicPatientAuth
{
    public string Mobile { get; set; } = string.Empty;
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
