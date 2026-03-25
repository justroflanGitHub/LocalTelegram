using Microsoft.EntityFrameworkCore;
using GroupService.Data;
using GroupService.Entities;
using GroupService.Models;
using System.Security.Cryptography;

namespace GroupService.Services;

/// <summary>
/// Service for managing groups
/// </summary>
public interface IGroupManagementService
{
    Task<Group> CreateGroupAsync(CreateGroupRequest request, Guid creatorId);
    Task<Group?> GetGroupAsync(Guid groupId);
    Task<Group?> GetGroupByInviteLinkAsync(string inviteLink);
    Task<GroupResponse> UpdateGroupAsync(Guid groupId, UpdateGroupRequest request, Guid userId);
    Task<bool> DeleteGroupAsync(Guid groupId, Guid userId);
    Task<IEnumerable<GroupListResponse>> GetUserGroupsAsync(Guid userId);
    Task<string> GenerateInviteLinkAsync(Guid groupId, Guid userId);
}

/// <summary>
/// Implementation of group management service
/// </summary>
public class GroupManagementService : IGroupManagementService
{
    private readonly GroupDbContext _context;
    private readonly ILogger<GroupManagementService> _logger;

    public GroupManagementService(GroupDbContext context, ILogger<GroupManagementService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new group
    /// </summary>
    public async Task<Group> CreateGroupAsync(CreateGroupRequest request, Guid creatorId)
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            MaxMembers = request.MaxMembers,
            SignMessages = request.SignMessages,
            AllowMembersToInvite = request.AllowMembersToInvite,
            OwnerId = creatorId,
            InviteLink = GenerateInviteCode(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Groups.Add(group);

        // Add creator as owner with Creator role
        var creatorMember = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = creatorId,
            Role = GroupRole.Creator,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };
        _context.GroupMembers.Add(creatorMember);

        // Add initial members if provided
        if (request.InitialMemberIds != null)
        {
            foreach (var memberId in request.InitialMemberIds.Where(id => id != creatorId))
            {
                var member = new GroupMember
                {
                    Id = Guid.NewGuid(),
                    GroupId = group.Id,
                    UserId = memberId,
                    Role = GroupRole.Member,
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _context.GroupMembers.Add(member);
            }
        }

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Group {GroupId} created by user {UserId}", group.Id, creatorId);
        
        return group;
    }

    /// <summary>
    /// Gets a group by ID
    /// </summary>
    public async Task<Group?> GetGroupAsync(Guid groupId)
    {
        return await _context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId);
    }

    /// <summary>
    /// Gets a group by invite link
    /// </summary>
    public async Task<Group?> GetGroupByInviteLinkAsync(string inviteLink)
    {
        return await _context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.InviteLink == inviteLink);
    }

    /// <summary>
    /// Updates group settings
    /// </summary>
    public async Task<GroupResponse> UpdateGroupAsync(Guid groupId, UpdateGroupRequest request, Guid userId)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null)
        {
            throw new InvalidOperationException("Group not found");
        }

        // Check if user has permission to update
        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
        
        if (member == null || member.Role < GroupRole.Admin)
        {
            throw new UnauthorizedAccessException("User does not have permission to update this group");
        }

        // Update fields
        if (request.Name != null)
            group.Name = request.Name;
        if (request.Description != null)
            group.Description = request.Description;
        if (request.AvatarUrl != null)
            group.AvatarUrl = request.AvatarUrl;
        if (request.Type.HasValue)
            group.Type = request.Type.Value;
        if (request.MaxMembers.HasValue)
            group.MaxMembers = request.MaxMembers.Value;
        if (request.SignMessages.HasValue)
            group.SignMessages = request.SignMessages.Value;
        if (request.AllowMembersToInvite.HasValue)
            group.AllowMembersToInvite = request.AllowMembersToInvite.Value;

        group.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Group {GroupId} updated by user {UserId}", groupId, userId);

        return MapToGroupResponse(group);
    }

    /// <summary>
    /// Deletes a group
    /// </summary>
    public async Task<bool> DeleteGroupAsync(Guid groupId, Guid userId)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null)
        {
            return false;
        }

        // Only creator can delete the group
        if (group.OwnerId != userId)
        {
            throw new UnauthorizedAccessException("Only the group creator can delete the group");
        }

        _context.Groups.Remove(group);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Group {GroupId} deleted by user {UserId}", groupId, userId);
        
        return true;
    }

    /// <summary>
    /// Gets all groups a user is a member of
    /// </summary>
    public async Task<IEnumerable<GroupListResponse>> GetUserGroupsAsync(Guid userId)
    {
        var groups = await _context.GroupMembers
            .Where(m => m.UserId == userId && m.IsActive)
            .Include(m => m.Group)
            .ThenInclude(g => g.Members)
            .Select(m => m.Group)
            .ToListAsync();

        return groups.Select(g => new GroupListResponse
        {
            Id = g.Id,
            Name = g.Name,
            AvatarUrl = g.AvatarUrl,
            Type = g.Type,
            MemberCount = g.Members.Count(m => m.IsActive),
            CreatedAt = g.CreatedAt
        });
    }

    /// <summary>
    /// Generates a new invite link for a group
    /// </summary>
    public async Task<string> GenerateInviteLinkAsync(Guid groupId, Guid userId)
    {
        var group = await _context.Groups.FindAsync(groupId);
        if (group == null)
        {
            throw new InvalidOperationException("Group not found");
        }

        // Check if user has permission
        var member = await _context.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId && m.IsActive);
        
        if (member == null || (member.Role < GroupRole.Admin && !group.AllowMembersToInvite))
        {
            throw new UnauthorizedAccessException("User does not have permission to generate invite links");
        }

        group.InviteLink = GenerateInviteCode();
        group.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("New invite link generated for group {GroupId} by user {UserId}", groupId, userId);
        
        return group.InviteLink;
    }

    private static string GenerateInviteCode()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "").Substring(0, 16);
    }

    private static GroupResponse MapToGroupResponse(Group group)
    {
        return new GroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            AvatarUrl = group.AvatarUrl,
            OwnerId = group.OwnerId,
            Type = group.Type,
            InviteLink = group.InviteLink,
            MaxMembers = group.MaxMembers,
            MemberCount = group.Members?.Count(m => m.IsActive) ?? 0,
            SignMessages = group.SignMessages,
            AllowMembersToInvite = group.AllowMembersToInvite,
            CreatedAt = group.CreatedAt,
            UpdatedAt = group.UpdatedAt
        };
    }
}
