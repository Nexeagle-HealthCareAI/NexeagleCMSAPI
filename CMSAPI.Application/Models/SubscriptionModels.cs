using System;

namespace CMSAPI.Application.Models;

public class RejectPaymentRequest
{
    public string Reason { get; set; } = string.Empty;
}

// CMS-initiated renewal — independent of the hospital-submitted-payment (PendingApproval) gate
// that ApprovePaymentAsync requires. Used for offline payments (cash/cheque/bank transfer already
// reconciled outside the app), reactivating an Expired/Blocked subscription, or correcting dates.
public class RenewSubscriptionRequest
{
    // Absolute override for the new validity end date. Null = compute from the plan's billing
    // cycle (Monthly/Quarterly/Half-Yearly/Yearly) starting from the later of today or the
    // subscription's current end date, so renewing early never forfeits already-paid time.
    public DateTime? SubscriptionEndDate { get; set; }

    // Required — doubles as the audit trail (HospitalSubscriptionPayments.Reference) explaining
    // why an admin renewed outside the normal payment-approval flow.
    public string Reference { get; set; } = string.Empty;

    public decimal? Amount { get; set; }
    public string? PaymentMode { get; set; }

    // The doctor/bed over-limit check (same one ApprovePaymentAsync enforces) is a warning here,
    // not a hard block — set true to proceed anyway after the admin has seen the warning.
    public bool AllowOverLimit { get; set; }
}
