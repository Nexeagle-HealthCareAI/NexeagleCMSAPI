using System;

namespace CMSAPI.Application.Models;

public class AiPitchRequest
{
    public Guid LeadId { get; set; }
}

public class AiObjectionRequest
{
    public Guid LeadId { get; set; }
    public string Objection { get; set; } = string.Empty;
}

public class AiSocialRequest
{
    public string Topic { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = "Hospital Owners";
}
