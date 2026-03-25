using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GroupService.Models;
using GroupService.Services;
using GroupService.Entities;
using System.Security.Claims;

namespace GroupService.Controllers;

/// <summary>
/// Controller for group management
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly IGroupManagementService _groupService;
    private readonly IGroupMemberService _memberService;
    private readonly IGroupInviteService _inviteService;
    private readonly ILogger<GroupsController> _logger;

    public GroupsController(
        IGroupManagementService groupService,
        IGroupMemberService memberService,
        IGroupInviteService inviteService,
        ILogger<GroupsController> logger)
    {
        _groupService = groupService;
        _memberService = memberService;
        _inviteService = inviteService;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    #region Groups

    /// <summary>
    /// Creates a new group
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GroupResponse>> CreateGroup([FromBody] CreateGroupRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var group = await _groupService.CreateGroupAsync(request, userId);
            
            var members = await _memberService.GetGroupMembersAsync(group.Id);
            
            return Ok(new GroupResponse
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                AvatarUrl = group.AvatarUrl,
                OwnerId = group.OwnerId,
                Type = group.Type,
                InviteLink = group.InviteLink,
                MaxMembers = group.MaxMembers,
                MemberCount = members.Count(),
                SignMessages = group.SignMessages,
                AllowMembersToInvite = group.AllowMembersToInvite,
                CreatedAt = group.CreatedAt,
                UpdatedAt = group.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets a group by ID
    /// </summary>
    [HttpGet("{groupId}")]
    public async Task<ActionResult<GroupResponse>> GetGroup(Guid groupId)
    {
        try
        {
            var group = await _groupService.GetGroupAsync(groupId);
            if (group == null)
            {
                return NotFound(new { error = "Group not found" });
            }

            var userId = GetCurrentUserId();
            
            // For private groups, only members can view
            if (group.Type == GroupType.Private)
            {
                var isMember = await _memberService.IsMemberAsync(groupId, userId);
                if (!isMember)
                {
                    return Forbid();
                }
            }

            return Ok(new GroupResponse
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
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Updates a group
    /// </summary>
    [HttpPut("{groupId}")]
    public async Task<ActionResult<GroupResponse>> UpdateGroup(Guid groupId, [FromBody] UpdateGroupRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var response = await _groupService.UpdateGroupAsync(groupId, request, userId);
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a group
    /// </summary>
    [HttpDelete("{groupId}")]
    public async Task<ActionResult> DeleteGroup(Guid groupId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _groupService.DeleteGroupAsync(groupId, userId);
            
            if (!result)
            {
                return NotFound(new { error = "Group not found" });
            }
            
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets all groups for the current user
    /// </summary>
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<GroupListResponse>>> GetMyGroups()
    {
        try
        {
            var userId = GetCurrentUserId();
            var groups = await _groupService.GetUserGroupsAsync(userId);
            return Ok(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user groups");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Generates a new invite link for a group
    /// </summary>
    [HttpPost("{groupId}/invite-link")]
    public async Task<ActionResult> RegenerateInviteLink(Guid groupId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var inviteLink = await _groupService.GenerateInviteLinkAsync(groupId, userId);
            return Ok(new { inviteLink });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating invite link for group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    #endregion

    #region Members

    /// <summary>
    /// Gets all members of a group
    /// </summary>
    [HttpGet("{groupId}/members")]
    public async Task<ActionResult<IEnumerable<GroupMemberResponse>>> GetMembers(Guid groupId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var members = await _memberService.GetGroupMembersAsync(groupId, userId);
            return Ok(members);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting members for group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Adds a member to a group
    /// </summary>
    [HttpPost("{groupId}/members")]
    public async Task<ActionResult<GroupMemberResponse>> AddMember(Guid groupId, [FromBody] AddMemberRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var member = await _memberService.AddMemberAsync(groupId, request, userId);
            return Ok(new GroupMemberResponse
            {
                Id = member.Id,
                GroupId = member.GroupId,
                UserId = member.UserId,
                Role = member.Role,
                CustomTitle = member.CustomTitle,
                IsMuted = member.IsMuted,
                MutedUntil = member.MutedUntil,
                JoinedAt = member.JoinedAt
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding member to group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Removes a member from a group
    /// </summary>
    [HttpDelete("{groupId}/members/{memberUserId}")]
    public async Task<ActionResult> RemoveMember(Guid groupId, Guid memberUserId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _memberService.RemoveMemberAsync(groupId, memberUserId, userId);
            
            if (!result)
            {
                return NotFound(new { error = "Member not found" });
            }
            
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing member from group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Leave a group
    /// </summary>
    [HttpPost("{groupId}/leave")]
    public async Task<ActionResult> LeaveGroup(Guid groupId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _memberService.LeaveGroupAsync(groupId, userId);
            
            if (!result)
            {
                return NotFound(new { error = "Not a member of this group" });
            }
            
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Updates a member's role
    /// </summary>
    [HttpPut("{groupId}/members/{memberUserId}/role")]
    public async Task<ActionResult<GroupMemberResponse>> UpdateMemberRole(
        Guid groupId, 
        Guid memberUserId, 
        [FromBody] UpdateMemberRoleRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var response = await _memberService.UpdateMemberRoleAsync(groupId, memberUserId, request, userId);
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating member role in group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Mutes a member
    /// </summary>
    [HttpPost("{groupId}/members/{memberUserId}/mute")]
    public async Task<ActionResult> MuteMember(Guid groupId, Guid memberUserId, [FromBody] MuteRequest? request = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            var duration = request?.DurationSeconds.HasValue == true 
                ? TimeSpan.FromSeconds(request.DurationSeconds.Value) 
                : (TimeSpan?)null;
            
            var result = await _memberService.MuteMemberAsync(groupId, memberUserId, userId, duration);
            
            if (!result)
            {
                return NotFound(new { error = "Member not found" });
            }
            
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error muting member in group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Unmutes a member
    /// </summary>
    [HttpPost("{groupId}/members/{memberUserId}/unmute")]
    public async Task<ActionResult> UnmuteMember(Guid groupId, Guid memberUserId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _memberService.UnmuteMemberAsync(groupId, memberUserId, userId);
            
            if (!result)
            {
                return NotFound(new { error = "Member not found" });
            }
            
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmuting member in group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    #endregion

    #region Invites

    /// <summary>
    /// Creates a new invite for a group
    /// </summary>
    [HttpPost("{groupId}/invites")]
    public async Task<ActionResult<GroupInviteResponse>> CreateInvite(Guid groupId, [FromBody] CreateInviteRequest? request = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            var invite = await _inviteService.CreateInviteAsync(groupId, request ?? new CreateInviteRequest(), userId);
            return Ok(new GroupInviteResponse
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
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating invite for group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets all invites for a group
    /// </summary>
    [HttpGet("{groupId}/invites")]
    public async Task<ActionResult<IEnumerable<GroupInviteResponse>>> GetInvites(Guid groupId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var invites = await _inviteService.GetGroupInvitesAsync(groupId, userId);
            return Ok(invites);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invites for group {GroupId}", groupId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Revokes an invite
    /// </summary>
    [HttpDelete("{groupId}/invites/{inviteId}")]
    public async Task<ActionResult> RevokeInvite(Guid groupId, Guid inviteId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _inviteService.RevokeInviteAsync(inviteId, userId);
            
            if (!result)
            {
                return NotFound(new { error = "Invite not found" });
            }
            
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking invite {InviteId}", inviteId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Joins a group using an invite code
    /// </summary>
    [HttpPost("join/{inviteCode}")]
    public async Task<ActionResult<GroupMemberResponse>> JoinGroup(string inviteCode)
    {
        try
        {
            var userId = GetCurrentUserId();
            var member = await _inviteService.JoinByInviteCodeAsync(inviteCode, userId);
            return Ok(new GroupMemberResponse
            {
                Id = member.Id,
                GroupId = member.GroupId,
                UserId = member.UserId,
                Role = member.Role,
                CustomTitle = member.CustomTitle,
                IsMuted = member.IsMuted,
                MutedUntil = member.MutedUntil,
                JoinedAt = member.JoinedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining group with invite code {InviteCode}", inviteCode);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Validates an invite code
    /// </summary>
    [HttpGet("validate-invite/{inviteCode}")]
    [AllowAnonymous]
    public async Task<ActionResult> ValidateInvite(string inviteCode)
    {
        try
        {
            var isValid = await _inviteService.ValidateInviteAsync(inviteCode);
            
            if (!isValid)
            {
                return Ok(new { valid = false, error = "Invite is not valid" });
            }

            var invite = await _inviteService.GetInviteByCodeAsync(inviteCode);
            if (invite == null)
            {
                return Ok(new { valid = false, error = "Invite not found" });
            }

            return Ok(new 
            { 
                valid = true, 
                groupId = invite.GroupId,
                groupName = invite.Group?.Name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating invite code {InviteCode}", inviteCode);
            return BadRequest(new { error = ex.Message });
        }
    }

    #endregion
}

/// <summary>
/// Request for muting a member
/// </summary>
public class MuteRequest
{
    public int? DurationSeconds { get; set; }
}
