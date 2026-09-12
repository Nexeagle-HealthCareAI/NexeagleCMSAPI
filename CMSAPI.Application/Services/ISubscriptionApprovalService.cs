using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Services;

public class ApprovalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }

    // Set when RenewSubscriptionAsync stops short of saving because the hospital exceeds the
    // plan's doctor/bed limits — the caller re-submits with AllowOverLimit=true to proceed anyway.
    public bool RequiresOverrideConfirmation { get; set; }
    public List<string> OverLimitDetails { get; set; } = new();
}

public interface ISubscriptionApprovalService
{
    Task<ApprovalResult> ApprovePaymentAsync(Guid hospitalId);
    Task<ApprovalResult> RejectPaymentAsync(Guid hospitalId, string reason);
    Task<ApprovalResult> RenewSubscriptionAsync(Guid hospitalId, RenewSubscriptionRequest request);
}
