namespace CMSAPI.Domain.Entities;

public class StatusMaster
{
    public string StatusCode { get; set; } = string.Empty;
    public string? StatusName { get; set; }

    // Navigation
    public System.Collections.Generic.List<Appointment>? Appointments { get; set; }
}
