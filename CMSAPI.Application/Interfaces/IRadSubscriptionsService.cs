using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>1Rad payment-request review, over 1RadAPI's service-key endpoints.</summary>
public interface IRadSubscriptionsService
{
    /// <summary>All 1Rad payment requests, newest first. Null when 1RadAPI can't be reached or refuses the key.</summary>
    Task<List<RadPaymentRequestItem>?> GetPaymentRequestsAsync();
    Task<RadReviewResult> ApproveAsync(Guid requestId, string? reviewNote);
    Task<RadReviewResult> RejectAsync(Guid requestId, string reason);
}
