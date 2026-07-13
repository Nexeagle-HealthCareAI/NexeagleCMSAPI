namespace CMSAPI.Models;

/// <summary>A hospital's subscription as surfaced by the CMS, with plan + hospital names resolved.</summary>
public sealed record HospitalSubscriptionDto
{
    public Guid HospitalSubscriptionId { get; init; }
    public string Platform { get; init; } = null!;
    public Guid HospitalId { get; init; }
    public string HospitalName { get; init; } = "";
    public Guid? PlanId { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Status after applying date-based expiry (what the UI should display).</summary>
    public string Status { get; init; } = null!;
    /// <summary>Raw status stored in the database.</summary>
    public string StoredStatus { get; init; } = null!;

    public DateTime? TrialStartDate { get; init; }
    public DateTime? TrialEndDate { get; init; }
    public DateTime? SubscriptionStartDate { get; init; }
    public DateTime? SubscriptionEndDate { get; init; }
    public DateTime? NextBillingDate { get; init; }
}

/// <summary>Per-platform subscription counts by effective status.</summary>
public sealed record PlatformSummaryDto
{
    public string Platform { get; init; } = null!;
    /// <summary>True when the platform's database could not be queried.</summary>
    public bool Available { get; init; } = true;
    public int Total { get; init; }
    public int Active { get; init; }
    public int Trial { get; init; }
    public int Pending { get; init; }
    public int Expired { get; init; }
    public int Blocked { get; init; }
}

public sealed record SubscriptionSummaryResponse
{
    public IReadOnlyList<PlatformSummaryDto> Platforms { get; init; } = Array.Empty<PlatformSummaryDto>();
    public PlatformSummaryDto Overall { get; init; } = new() { Platform = "All" };
}

/// <summary>Activate (true) or deactivate/block (false) a hospital's subscription.</summary>
public sealed record SetStatusRequest
{
    public bool Active { get; init; }
}

/// <summary>Set or extend the trial window.</summary>
public sealed record SetTrialRequest
{
    public DateTime? TrialStartDate { get; init; }
    public DateTime? TrialEndDate { get; init; }
}

/// <summary>Set the paid subscription validity window (and optional next billing date).</summary>
public sealed record SetValidityRequest
{
    public DateTime? SubscriptionStartDate { get; init; }
    public DateTime? SubscriptionEndDate { get; init; }
    public DateTime? NextBillingDate { get; init; }
}

/// <summary>Assign or change the hospital's plan.</summary>
public sealed record AssignPlanRequest
{
    public Guid PlanId { get; init; }
}
