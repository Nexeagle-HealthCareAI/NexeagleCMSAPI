using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Services;

public class ApprovalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }
}

public interface ISubscriptionApprovalService
{
    Task<ApprovalResult> ApprovePaymentAsync(Guid hospitalId);
    Task<ApprovalResult> RejectPaymentAsync(Guid hospitalId, string reason);
}
