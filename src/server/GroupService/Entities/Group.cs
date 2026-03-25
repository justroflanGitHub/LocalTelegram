namespace GroupService.Entities;

/// <summary>
/// Represents a group chat
/// </summary>
public class Group
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid OwnerId { get; set; }
    public GroupType Type { get; set; } = GroupType.Public;
    public string? InviteLink { get; set; }
    public int MaxMembers { get; set; } = 200;
    public bool SignMessages { get; set; } = false;
    public bool AllowMembersToInvite { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<GroupInvite> Invites { get; set; } = new List<GroupInvite>();
}

/// <summary>
/// Type of group
/// </summary>
public enum GroupType
{
    /// <summary>
    /// Public group - anyone can join via invite link
    /// </summary>
    Public = 0,
    
    /// <summary>
    /// Private group - only invited members can join
    /// </summary>
    Private = 1,
    
    /// <summary>
    /// Broadcast channel - only admins can post
    /// </summary>
    Channel = 2
}
