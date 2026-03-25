using GroupService.Entities;

namespace GroupService.Models;

/// <summary>
/// Request to create a new group
/// </summary>
public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GroupType Type { get; set; } = GroupType.Public;
    public int MaxMembers { get; set; } = 200;
    public bool SignMessages { get; set; } = false;
    public bool AllowMembersToInvite { get; set; } = true;
    public List<Guid>? InitialMemberIds { get; set; }
}

/// <summary>
/// Request to update group settings
/// </summary>
public class UpdateGroupRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public GroupType? Type { get; set; }
    public int? MaxMembers { get; set; }
    public bool? SignMessages { get; set; }
    public bool? AllowMembersToInvite { get; set; }
}

/// <summary>
/// Request to add a member to a group
/// </summary>
public class AddMemberRequest
{
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public string? CustomTitle { get; set; }
}

/// <summary>
/// Request to update a member's role
/// </summary>
public class UpdateMemberRoleRequest
{
    public GroupRole Role { get; set; }
    public string? CustomTitle { get; set; }
}

/// <summary>
/// Request to create an invite link
/// </summary>
public class CreateInviteRequest
{
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
}

/// <summary>
/// Response for a group
/// </summary>
public class GroupResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid OwnerId { get; set; }
    public GroupType Type { get; set; }
    public string? InviteLink { get; set; }
    public int MaxMembers { get; set; }
    public int MemberCount { get; set; }
    public bool SignMessages { get; set; }
    public bool AllowMembersToInvite { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Response for a group member
/// </summary>
public class GroupMemberResponse
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public GroupRole Role { get; set; }
    public string? CustomTitle { get; set; }
    public bool IsMuted { get; set; }
    public DateTime? MutedUntil { get; set; }
    public DateTime JoinedAt { get; set; }
}

/// <summary>
/// Response for a group invite
/// </summary>
public class GroupInviteResponse
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public string InviteLink { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int CurrentUses { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Group info for listing
/// </summary>
public class GroupListResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public GroupType Type { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
