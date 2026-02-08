using System;

namespace CMSAPI.Application.Models;

public class HospitalListItem
{
    public Guid Id { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int TotalPatients { get; set; }
    public DateTime RegisteredOn { get; set; }
    public string Status { get; set; } = "Active"; // Active | Pending
}
