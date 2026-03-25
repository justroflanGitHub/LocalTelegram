using Microsoft.EntityFrameworkCore;
using GroupService.Data;
using GroupService.Entities;
using GroupService.Models;
using System.Security.Cryptography;

namespace GroupService.Services;

/// <summary>
/// Service for managing group invites
/// </summary>
public interface IGroupInviteService
{
    Task<GroupInvite> CreateInviteAsync(Guid groupId, CreateInviteRequest request, Guid createdBy);
    Task<GroupInviteResponse?> GetInviteAsync(Guid inviteId);
    Task<GroupInvite?> GetInviteByCodeAsync(string inviteCode);
    Task<bool> RevokeInviteAsync(Guid inviteId, Guid revokedBy);
    Task<IEnumerable<GroupInviteResponse>> GetGroupInvitesAsync(Guid groupId, Guid requestingUserId);
    Task<GroupMember> JoinByInviteCodeAsync(string inviteCode, Guid userId);
    Task<bool> ValidateInviteAsync(string inviteCode);
}

/// <summary>
/// Implementation of group invite service
/// </summary>
public class GroupInviteService : IGroupInviteService
{
    private readonly GroupDbContext _context;
    private readonly IGroupMemberService _memberService;
    private readonly ILogger<GroupInviteService> _logger;

    public GroupInviteService(
        GroupDbContext context, 
        IGroupMemberService memberService,
        ILogger<GroupInviteService> logger)
    {
        _context = context;
        _memberService = memberService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new invite for a group
    /// </summary>
    public async Task<GroupInvite> CreateInviteAsync(Guid groupId, CreateInviteRequest request, Guid createdBy)
    {
        // Check if user has permission
        var hasPermission = await _memberService.HasPermissionAsync(groupId, createdBy, GroupRole.Admin);
        var group = await _context.Groups.FindAsync(groupId);
        
        if (group == null)
        {
            throw new InvalidOperationException("Group not found");
        }

        if (!hasPermission && !group.AllowMembersToInvite)
        {
            throw new UnauthorizedAccessException("User does not have permission to create invites");
        }

        var invite = new GroupInvite
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            InviteCode = GenerateInviteCode(),
            CreatedBy = createdBy,
            ExpiresAt = request.ExpiresAt,
            MaxUses = request.MaxUses,
            CurrentUses = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.GroupInvites.Add(invite);
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Invite {InviteId} created for group {GroupId} by user {UserId}", 
            invite.Id, groupId, createdBy);
        
        return invite;
    }

    /// <summary>
    /// Gets an invite by ID
    /// </summary>
    public async Task<GroupInviteResponse?> GetInviteAsync(Guid inviteId)
    {
        var invite = await _context.GroupInvites.FindAsync(inviteId);
        return invite != null ? MapToInviteResponse(invite) : null;
    }

    /// <summary>
    /// Gets an invite by code
    /// </summary>
    public async Task<GroupInvite?> GetInviteByCodeAsync(string inviteCode)
    {
        return await _context.GroupInvites
            .Include(i => i.Group)
            .FirstOrDefaultAsync(i => i.InviteCode == inviteCode);
    }

    /// <summary>
    /// Revokes an invite
    /// </summary>
    public async Task<bool> RevokeInviteAsync(Guid inviteId, Guid revokedBy)
    {
        var invite = await _context.GroupInvites.FindAsync(inviteId);
        if (invite == null)
        {
            return false;
        }

        // Check if user has permission (admin or creator of invite)
        var hasPermission = await _memberService.HasPermissionAsync(invite.GroupId, revokedBy, GroupRole.Admin);
        if (!hasPermission && invite.CreatedBy != revokedBy)
        {
            throw new UnauthorizedAccessException("User does not have permission to revoke this invite");
        }

        invite.IsActive = false;
        await _context.SaveChangesAsync();
        
        _logger.LogInformation("Invite {InviteId} revoked by user {UserId}", inviteId, revokedBy);
        
        return true;
    }

    /// <summary>
    /// Gets all invites for a group
    /// </summary>
    public async Task<IEnumerable<GroupInviteResponse>> GetGroupInvitesAsync(Guid groupId, Guid requestingUserId)
    {
        var hasPermission = await _memberService.HasPermissionAsync(groupId, requestingUserId, GroupRole.Admin);
        if (!hasPermission)
        {
            throw new UnauthorizedAccessException("User does not have permission to view invites");
        }

        var invites = await _context.GroupInvites
            .Where(i => i.GroupId == groupId)
            .ToListAsync();

        return invites.Select(MapToInviteResponse);
    }

    /// <summary>
    /// Joins a group using an invite code
    /// </summary>
    public async Task<GroupMember> JoinByInviteCodeAsync(string inviteCode, Guid userId)
    {
        var invite = await GetInviteByCodeAsync(inviteCode);
        if (invite == null)
        {
            throw new InvalidOperationException("Invalid invite code");
        }

        if (!await ValidateInviteAsync(inviteCode))
        {
            throw new InvalidOperationException("Invite is no longer valid");
        }

        var group = invite.Group;
        
        // Check if already a member
        var isMember = await _memberService.IsMemberAsync(group.Id, userId);
        if (isMember)
        {
            throw new InvalidOperationException("User is already a member of this group");
        }

        // Check if group is full
        var memberCount = await _memberService.GetMemberCountAsync(group.Id);
        if (memberCount >= group.MaxMembers)
        {
            throw new InvalidOperationException("Group has reached maximum member limit");
        }

        // Add member
        var member = await _memberService.AddMemberAsync(group.Id, new AddMemberRequest
        {
            UserId = userId,
            Role = GroupRole.Member
        }, invite.CreatedBy);

        // Update invite usage
        invite.CurrentUses++;
        if (invite.MaxUses.HasValue && invite.CurrentUses >= invite.MaxUses.Value)
        {
            invite.IsActive = false;
        }

        await _context.SaveChangesAsync();
        
        _logger.LogInformation("User {UserId} joined group {GroupId} using invite {InviteCode}", 
            userId, group.Id, inviteCode);
        
        return member;
    }

    /// <summary>
    /// Validates if an invite is still usable
    /// </summary>
    public async Task<bool> ValidateInviteAsync(string inviteCode)
    {
        var invite = await GetInviteByCodeAsync(inviteCode);
        if (invite == null)
        {
            return false;
        }

        if (!invite.IsActive)
        {
            return false;
        }

        if (invite.ExpiresAt.HasValue && invite.ExpiresAt.Value < DateTime.UtcNow)
        {
            return false;
        }

        if (invite.MaxUses.HasValue && invite.CurrentUses >= invite.MaxUses.Value)
        {
            return false;
        }

        return true;
    }

    private static string GenerateInviteCode()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[8];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "").Substring(0, 10);
    }

    private static GroupInviteResponse MapToInviteResponse(GroupInvite invite)
    {
        return new GroupInviteResponse
        {
            Id = invite.Id,
            GroupId = invite.GroupId,
            InviteCode = invite.InviteCode,
            InviteLink = $"https://localtelegram.local/join/{invite.InviteCode}",
            CreatedBy = invite.CreatedBy,
            CreatedAt = invite.CreatedAt,
            ExpiresAt = invite.ExpiresAt,
            MaxUses = invite.MaxUses,
            CurrentUses = invite.CurrentUses,
            IsActive = invite.IsActive
        };
    }
}
