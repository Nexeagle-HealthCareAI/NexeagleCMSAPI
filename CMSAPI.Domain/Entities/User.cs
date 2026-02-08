using System;

namespace CMSAPI.Domain.Entities;

public class User
{
    public Guid UserID { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int UserStatusId { get; set; }
    public DateTime CreatedAt { get; set; }
}
