using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Domain.Entities;

public class UserProfile
{
    public Guid UserProfileID { get; set; }
    public Guid UserID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? Language { get; set; }
    public string? ProfilePictureURL { get; set; }
    public string? EmployeeID { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? BloodGroup { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactNumber { get; set; }
    public int ProfileCompletionPercent { get; set; }
    public int UserStatusId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    [NotMapped]
    public object? User { get; set; }
}
