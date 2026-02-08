using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Domain.Entities;

public class UserAuth
{
    public Guid UserAuthID { get; set; }
    public Guid UserID { get; set; }
    public string? HashedPassword { get; set; }
    public string? LoginMethod { get; set; }
    public string? Otp { get; set; }
    public DateTime? OtpSentDateTime { get; set; }
    public bool IsOtpUsed { get; set; }
    public int FailedLoginAttempts { get; set; }
    public bool IsLocked { get; set; }
    public string? LastLoginIP { get; set; }
    public DateTime? LastLoginTime { get; set; }
    public DateTime? OtpExpireAt { get; set; }
    public DateTime? PasswordSetAt { get; set; }
    public int UserStatusId { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation placeholder for User (entity not defined yet)
    [NotMapped]
    public object? User { get; set; }
}
