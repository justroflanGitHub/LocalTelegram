using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserService.Models;
using UserService.Services;
using System.Security.Claims;

namespace UserService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserProfileService _profileService;
    private readonly IContactService _contactService;
    private readonly IBlockService _blockService;
    private readonly IRedisStatusService _statusService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserProfileService profileService,
        IContactService contactService,
        IBlockService blockService,
        IRedisStatusService statusService,
        ILogger<UsersController> logger)
    {
        _profileService = profileService;
        _contactService = contactService;
        _blockService = blockService;
        _statusService = statusService;
        _logger = logger;
    }

    /// <summary>
    /// Get current user's profile
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> GetMyProfile()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var profile = await _profileService.GetProfileDtoAsync(userId.Value);
        if (profile == null) return NotFound();

        return Ok(profile);
    }

    /// <summary>
    /// Update current user's profile
    /// </summary>
    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var profile = await _profileService.UpdateProfileAsync(userId.Value, request);
        if (profile == null) return NotFound();

        var dto = await _profileService.GetProfileDtoAsync(userId.Value);
        return Ok(dto);
    }

    /// <summary>
    /// Get user profile by ID
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<ActionResult<UserProfileDto>> GetUserProfile(long userId)
    {
        var currentUserId = GetUserId();
        if (currentUserId == null) return Unauthorized();

        var profile = await _profileService.GetProfileDtoAsync(userId, currentUserId.Value);
        if (profile == null) return NotFound();

        return Ok(profile);
    }

    /// <summary>
    /// Search for users
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<UserProfileDto>>> SearchUsers([FromQuery] string query, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return BadRequest("Query must be at least 2 characters");
        }

        var results = await _profileService.SearchUsersAsync(query, limit);
        return Ok(results);
    }

    /// <summary>
    /// Set user avatar
    /// </summary>
    [HttpPost("me/avatar")]
    public async Task<ActionResult<UserAvatar>> SetAvatar([FromBody] SetAvatarRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var avatar = await _profileService.SetAvatarAsync(userId.Value, request.FileId, request.SmallFileId);
        return Ok(avatar);
    }

    /// <summary>
    /// Delete user avatar
    /// </summary>
    [HttpDelete("me/avatar")]
    public async Task<ActionResult> DeleteAvatar()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _profileService.DeleteAvatarAsync(userId.Value);
        if (!success) return NotFound();

        return Ok();
    }

    /// <summary>
    /// Get user's contacts
    /// </summary>
    [HttpGet("me/contacts")]
    public async Task<ActionResult<List<ContactDto>>> GetContacts()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var contacts = await _contactService.GetContactsAsync(userId.Value);
        return Ok(contacts);
    }

    /// <summary>
    /// Add a contact
    /// </summary>
    [HttpPost("me/contacts")]
    public async Task<ActionResult<ContactDto>> AddContact([FromBody] AddContactRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var contact = await _contactService.AddContactAsync(userId.Value, request.ContactUserId, request.ContactName);
        if (contact == null) return BadRequest();

        return Ok(contact);
    }

    /// <summary>
    /// Remove a contact
    /// </summary>
    [HttpDelete("me/contacts/{contactUserId}")]
    public async Task<ActionResult> RemoveContact(long contactUserId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _contactService.RemoveContactAsync(userId.Value, contactUserId);
        if (!success) return NotFound();

        return Ok();
    }

    /// <summary>
    /// Toggle contact favorite status
    /// </summary>
    [HttpPost("me/contacts/{contactUserId}/favorite")]
    public async Task<ActionResult> ToggleFavorite(long contactUserId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _contactService.ToggleFavoriteAsync(userId.Value, contactUserId);
        if (!success) return NotFound();

        return Ok();
    }

    /// <summary>
    /// Block a user
    /// </summary>
    [HttpPost("me/blocked")]
    public async Task<ActionResult> BlockUser([FromBody] BlockUserRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _blockService.BlockUserAsync(userId.Value, request.UserId);
        return Ok();
    }

    /// <summary>
    /// Unblock a user
    /// </summary>
    [HttpDelete("me/blocked/{blockedUserId}")]
    public async Task<ActionResult> UnblockUser(long blockedUserId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _blockService.UnblockUserAsync(userId.Value, blockedUserId);
        if (!success) return NotFound();

        return Ok();
    }

    /// <summary>
    /// Get blocked users
    /// </summary>
    [HttpGet("me/blocked")]
    public async Task<ActionResult<List<BlockedUserDto>>> GetBlockedUsers()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var blocked = await _blockService.GetBlockedUsersAsync(userId.Value);
        return Ok(blocked);
    }

    /// <summary>
    /// Get users' online status
    /// </summary>
    [HttpPost("status")]
    public async Task<ActionResult<Dictionary<long, UserStatus>>> GetUsersStatus([FromBody] long[] userIds)
    {
        var statuses = await _statusService.GetUsersStatusAsync(userIds);
        return Ok(statuses);
    }

    /// <summary>
    /// Update current user's status
    /// </summary>
    [HttpPut("me/status")]
    public async Task<ActionResult> UpdateStatus([FromBody] UpdateStatusRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _statusService.SetUserStatusAsync(userId.Value, request.Status);
        return Ok();
    }

    private long? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return userId;
    }
}

public class SetAvatarRequest
{
    public string FileId { get; set; } = string.Empty;
    public string SmallFileId { get; set; } = string.Empty;
}

public class AddContactRequest
{
    public long ContactUserId { get; set; }
    public string? ContactName { get; set; }
}

public class BlockUserRequest
{
    public long UserId { get; set; }
}

public class UpdateStatusRequest
{
    public UserStatus Status { get; set; }
}
