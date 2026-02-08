namespace CMSAPI.Domain.Entities;

public class UserStatus
{
    public int UserStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;

    // Navigation (optional)
    public System.Collections.Generic.List<UserProfile>? UserProfiles { get; set; }
    public System.Collections.Generic.List<UserHistory>? UserHistories { get; set; }
}
