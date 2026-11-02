using System;

namespace CMSAPI.Application.Models;

// Mirrors 1RadAPI's PaymentRequestDto (Features/Subscriptions/Queries/GetAllPaymentRequests).
// Two separate services over HTTP, so the shape is kept in sync by hand.
public class RadPaymentRequestItem
{
    public Guid RequestId { get; set; }
    public Guid HospitalId { get; set; }
    public string HospitalName { get; set; } = string.Empty;
    public Guid? PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;          // Pending | Approved | Rejected
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public string PaymentMode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Modules { get; set; }
    public int StorageOverageGb { get; set; }
    public decimal StorageOverageAmount { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public string PayerContact { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class RadReviewResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int StatusCode { get; set; }
}
