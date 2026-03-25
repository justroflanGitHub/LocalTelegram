namespace GroupService.Entities;

/// <summary>
/// Represents a member of a group
/// </summary>
public class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public string? CustomTitle { get; set; }
    public bool IsMuted { get; set; } = false;
    public DateTime? MutedUntil { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public Group Group { get; set; } = null!;
}

/// <summary>
/// Role of a group member
/// </summary>
public enum GroupRole
{
    /// <summary>
    /// Regular member - can send messages
    /// </summary>
    Member = 0,
    
    /// <summary>
    /// Moderator - can delete messages, kick members
    /// </summary>
    Moderator = 1,
    
    /// <summary>
    /// Administrator - can add/remove admins, change group settings
    /// </summary>
    Admin = 2,
    
    /// <summary>
    /// Creator - full control over the group
    /// </summary>
    Creator = 3
}
