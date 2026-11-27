using CMSAPI.Application.Services;

namespace CMSAPI.Models;

/// <summary>Calculator input: platform, capacity, and the enabled module keys.</summary>
public sealed record CalculatePlanRequest
{
    public string ApplicationName { get; init; } = "";
    public int Doctors { get; init; }
    public int Beds { get; init; }
    public List<string> Modules { get; init; } = new();
}

/// <summary>A predefined tier that fits the requested capacity.</summary>
public sealed record MatchedTierDto
{
    public Guid PlanId { get; init; }
    public string Name { get; init; } = "";
    public int MaxDoctors { get; init; }
    public int MaxBeds { get; init; }
    public decimal Price { get; init; }
}

public sealed record CalculatePlanResponse
{
    public PriceBreakdown Breakdown { get; init; } = null!;
    /// <summary>Undiscounted list price (250/doctor + 25/bed + modules), for showing savings.</summary>
    public decimal BasePrice { get; init; }
    /// <summary>The smallest predefined tier that fits, if any.</summary>
    public MatchedTierDto? MatchedTier { get; init; }
}

/// <summary>Commit a selection. Set <see cref="PlanId"/> to pick an existing tier; otherwise a custom plan is minted.</summary>
public sealed record ChoosePlanRequest
{
    public string ApplicationName { get; init; } = "";
    public Guid? PlanId { get; init; }
    public int Doctors { get; init; }
    public int Beds { get; init; }
    public List<string> Modules { get; init; } = new();
    public string? BillingCycle { get; init; }
}

public sealed record ChoosePlanResponse
{
    public Guid PlanId { get; init; }
    public bool IsCustom { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
}

public sealed record ModuleChargeDto
{
    public string Module { get; init; } = "";
    public decimal Charge { get; init; }
}

public sealed record PutModuleChargesRequest
{
    public string ApplicationName { get; init; } = "";
    public List<ModuleChargeDto> Charges { get; init; } = new();
}
