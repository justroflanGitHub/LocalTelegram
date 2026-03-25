using Microsoft.EntityFrameworkCore;
using GroupService.Data;
using GroupService.Entities;
using GroupService.Models;

namespace GroupService.Services;

/// <summary>
/// Service for managing group members
/// </summary>
public interface IGroupMemberService
{
    Task<GroupMember> AddMemberAsync(Guid groupId, AddMemberRequest request, Guid addedBy);
    Task<bool> RemoveMemberAsync(Guid groupId, Guid userId, Guid removedBy);
    Task<bool> LeaveGroupAsync(Guid groupId, Guid userId);
    Task<GroupMemberResponse> UpdateMemberRoleAsync(Guid groupId, Guid memberId, UpdateMemberRoleRequest request, Guid updatedBy);
    Task<IEnumerable<GroupMemberResponse>> GetGroupMembersAsync(Guid groupId, Guid? requestingUserId = null);
    Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId);
    Task<bool> IsMemberAsync(Guid groupId, Guid userId);
    Task<bool> HasPermissionAsync(Guid groupId, Guid userId, GroupRole requiredRole);
    Task<int> GetMemberCountAsync(Guid groupId);
    Task<bool> MuteMemberAsync(Guid groupId, Guid userId, Guid mutedBy, TimeSpan? duration = null);
    Task<bool> UnmuteMemberAsync(Guid groupId, Guid userId, Guid unmutedBy);
}

/// <summary>
/// Implementation of group member service
/// </summary>
public class GroupMemberService : IGroupMemberService
{
    private readonly GroupDbContext _context;
    private readonly ILogger<GroupMemberService> _logger;

    public GroupMemberService(GroupDbContext context, ILogger<GroupMemberService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Adds a member to a group
    /// </summary>
    public async Task<GroupMember> AddMemberAsync(Guid groupId, AddMemberRequest request, Guid addedBy)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null)
        {
            throw new InvalidOperationException("Group not found");
        }

        // Check if user has permission to add members
        var adderMember = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == addedBy && m.IsActive);
        
        if (adderMember == null || (adderMember.Role < GroupRole.Admin && !group.AllowMembersToInvite))
        {
            throw new UnauthorizedAccessException("User does not have permission to add members to this group");
        }

        // Check if group is full
        var currentCount = await GetMemberCountAsync(groupId);
        if (currentCount >= group.MaxMembers)
        {
            throw new InvalidOperationException("Group has reached maximum member limit");
        }

        // Check if user is already a member
        var existingMember = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == request.UserId);
        
        if (existingMember != null)
        {
            if (existingMember.IsActive)
            {
                throw new InvalidOperationException("User is already a member of this group");
            }
            // Reactivate membership
            existingMember.IsActive = true;
            existingMember.Role = request.Role;
            existingMember.CustomTitle = request.CustomTitle;
            existingMember.JoinedAt = DateTime.UtcNow;
            existingMember.LeftAt = null;
            
            await _context.SaveChangesAsync();
            _logger.LogInformation("User {UserId} reactivated in group {GroupId}", request.UserId, groupId);
            return existingMember;
        }

        // Add new member
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            UserId = request.UserId,
            Role = request.Role,
            CustomTitle = request.CustomTitle,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.GroupMembers.Add(member);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("User {UserId} added to group {GroupId} by {AddedBy}", request.UserId, groupId, addedBy);
        
        return member;
    }

    /// <summary>
    /// Removes a member from a group (kick)
    /// </summary>
    public async Task<bool> RemoveMemberAsync(Guid groupId, Guid userId, Guid removedBy)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null)
        {
            return false;
        }

        var remover = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == removedBy && m.IsActive);
        
        if (remover == null || remover.Role < GroupRole.Moderator)
        {
            throw new UnauthorizedAccessException("User does not have permission to remove members");
        }

        var memberToRemove = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
        
        if (memberToRemove == null)
        {
            return false;
        }

        // Can't remove someone with higher or equal role
        if (memberToRemove.Role >= remover.Role && userId != removedBy)
        {
            throw new UnauthorizedAccessException("Cannot remove a member with equal or higher role");
        }

        // Can't remove the creator
        if (memberToRemove.Role == GroupRole.Creator)
        {
            throw new UnauthorizedAccessException("Cannot remove the group creator");
        }

        memberToRemove.IsActive = false;
        memberToRemove.LeftAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("User {UserId} removed from group {GroupId} by {RemovedBy}", userId, groupId, removedBy);
        
        return true;
    }

    /// <summary>
    /// User leaves a group
    /// </summary>
    public async Task<bool> LeaveGroupAsync(Guid groupId, Guid userId)
    {
        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
        
        if (member == null)
        {
            return false;
        }

        // Creator can't leave, must transfer ownership or delete group
        if (member.Role == GroupRole.Creator)
        {
            throw new InvalidOperationException("Group creator cannot leave. Transfer ownership or delete the group instead.");
        }

        member.IsActive = false;
        member.LeftAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("User {UserId} left group {GroupId}", userId, groupId);
        
        return true;
    }

    /// <summary>
    /// Updates a member's role
    /// </summary>
    public async Task<GroupMemberResponse> UpdateMemberRoleAsync(Guid groupId, Guid memberId, UpdateMemberRoleRequest request, Guid updatedBy)
    {
        var updater = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == updatedBy && m.IsActive);
        
        if (updater == null || updater.Role < GroupRole.Admin)
        {
            throw new UnauthorizedAccessException("User does not have permission to update member roles");
        }

        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == memberId && m.IsActive);
        
        if (member == null)
        {
            throw new InvalidOperationException("Member not found");
        }

        // Can't modify someone with higher or equal role
        if (member.Role >= updater.Role)
        {
            throw new UnauthorizedAccessException("Cannot modify a member with equal or higher role");
        }

        // Can't assign a role equal or higher than own role
        if (request.Role >= updater.Role)
        {
            throw new UnauthorizedAccessException("Cannot assign a role equal or higher than your own");
        }

        member.Role = request.Role;
        member.CustomTitle = request.CustomTitle;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Member {MemberId} role updated to {Role} in group {GroupId} by {UpdatedBy}", 
            memberId, request.Role, groupId, updatedBy);
        
        return MapToMemberResponse(member);
    }

    /// <summary>
    /// Gets all members of a group
    /// </summary>
    public async Task<IEnumerable<GroupMemberResponse>> GetGroupMembersAsync(Guid groupId, Guid? requestingUserId = null)
    {
        var query = _context.GroupMembers
            .Where(m => m.GroupId == groupId && m.IsActive);

        // For private groups, only members can see the member list
        var group = await _context.Groups.FindAsync(groupId);
        if (group != null && group.Type == GroupType.Private && requestingUserId.HasValue)
        {
            var isMember = await IsMemberAsync(groupId, requestingUserId.Value);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("Only group members can view the member list");
            }
        }

        var members = await query.ToListAsync();
        return members.Select(MapToMemberResponse);
    }

    /// <summary>
    /// Gets a specific member
    /// </summary>
    public async Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId)
    {
        return await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
    }

    /// <summary>
    /// Checks if a user is a member of a group
    /// </summary>
    public async Task<bool> IsMemberAsync(Guid groupId, Guid userId)
    {
        return await _context.GroupMembers
            .AnyAsync(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
    }

    /// <summary>
    /// Checks if a user has a specific role or higher in a group
    /// </summary>
    public async Task<bool> HasPermissionAsync(Guid groupId, Guid userId, GroupRole requiredRole)
    {
        var member = await GetMemberAsync(groupId, userId);
        return member != null && member.Role >= requiredRole;
    }

    /// <summary>
    /// Gets the current member count of a group
    /// </summary>
    public async Task<int> GetMemberCountAsync(Guid groupId)
    {
        return await _context.GroupMembers
            .CountAsync(m => m.GroupId == groupId && m.IsActive);
    }

    /// <summary>
    /// Mutes a member
    /// </summary>
    public async Task<bool> MuteMemberAsync(Guid groupId, Guid userId, Guid mutedBy, TimeSpan? duration = null)
    {
        var muter = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == mutedBy && m.IsActive);
        
        if (muter == null || muter.Role < GroupRole.Moderator)
        {
            throw new UnauthorizedAccessException("User does not have permission to mute members");
        }

        var member = await GetMemberAsync(groupId, userId);
        if (member == null)
        {
            return false;
        }

        if (member.Role >= muter.Role)
        {
            throw new UnauthorizedAccessException("Cannot mute a member with equal or higher role");
        }

        member.IsMuted = true;
        member.MutedUntil = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("User {UserId} muted in group {GroupId} by {MutedBy}", userId, groupId, mutedBy);
        
        return true;
    }

    /// <summary>
    /// Unmutes a member
    /// </summary>
    public async Task<bool> UnmuteMemberAsync(Guid groupId, Guid userId, Guid unmutedBy)
    {
        var unmuter = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == unmutedBy && m.IsActive);
        
        if (unmuter == null || unmuter.Role < GroupRole.Moderator)
        {
            throw new UnauthorizedAccessException("User does not have permission to unmute members");
        }

        var member = await GetMemberAsync(groupId, userId);
        if (member == null)
        {
            return false;
        }

        member.IsMuted = false;
        member.MutedUntil = null;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("User {UserId} unmuted in group {GroupId} by {UnmutedBy}", userId, groupId, unmutedBy);
        
        return true;
    }

    private static GroupMemberResponse MapToMemberResponse(GroupMember member)
    {
        return new GroupMemberResponse
        {
            Id = member.Id,
            GroupId = member.GroupId,
            UserId = member.UserId,
            Role = member.Role,
            CustomTitle = member.CustomTitle,
            IsMuted = member.IsMuted,
            MutedUntil = member.MutedUntil,
            JoinedAt = member.JoinedAt
        };
    }
}
