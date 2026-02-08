using System;

namespace CMSAPI.Domain.Entities;

public class UserHistory
{
    public Guid UserId { get; set; }
    public int UserStatusId { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTime UpdatedDate { get; set; }

    // Navigation
    public UserStatus? UserStatus { get; set; }
}
