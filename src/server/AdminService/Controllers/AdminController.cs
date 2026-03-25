using AdminService.Models;
using AdminService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AdminService.Controllers;

/// <summary>
/// Admin API controller for user, group, and system management
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILdapService _ldapService;
    private readonly ILogger<AdminController> _logger;
    
    public AdminController(
        IAdminService adminService,
        ILdapService ldapService,
        ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _ldapService = ldapService;
        _logger = logger;
    }
    
    #region Users
    
    /// <summary>
    /// Gets paginated list of users
    /// </summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResult<AdminUser>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers([FromQuery] UserListRequest request)
    {
        var result = await _adminService.GetUsersAsync(request);
        return Ok(result);
    }
    
    /// <summary>
    /// Gets user by ID
    /// </summary>
    [HttpGet("users/{id}")]
    [ProducesResponseType(typeof(AdminUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(long id)
    {
        var user = await _adminService.GetUserAsync(id);
        if (user == null)
            return NotFound();
        return Ok(user);
    }
    
    /// <summary>
    /// Creates a new user
    /// </summary>
    [HttpPost("users")]
    [ProducesResponseType(typeof(AdminUser), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var user = await _adminService.CreateUserAsync(request, actorId);
            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
    
    /// <summary>
    /// Updates user
    /// </summary>
    [HttpPut("users/{id}")]
    [ProducesResponseType(typeof(AdminUser), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(long id, [FromBody] UpdateUserRequest request)
    {
        var actorId = GetCurrentUserId();
        var user = await _adminService.UpdateUserAsync(id, request, actorId);
        if (user == null)
            return NotFound();
        return Ok(user);
    }
    
    /// <summary>
    /// Deletes user (soft delete)
    /// </summary>
    [HttpDelete("users/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(long id)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.DeleteUserAsync(id, actorId);
        if (!result)
            return NotFound();
        return NoContent();
    }
    
    /// <summary>
    /// Resets user password
    /// </summary>
    [HttpPost("users/{id}/reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest request)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.ResetUserPasswordAsync(id, request, actorId);
        if (!result)
            return NotFound();
        return Ok(new { message = "Password reset successfully" });
    }
    
    /// <summary>
    /// Suspends user
    /// </summary>
    [HttpPost("users/{id}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendUser(long id, [FromBody] SuspendRequest request)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.SuspendUserAsync(id, request.Reason, actorId);
        if (!result)
            return NotFound();
        return Ok(new { message = "User suspended successfully" });
    }
    
    /// <summary>
    /// Activates user
    /// </summary>
    [HttpPost("users/{id}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser(long id)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.ActivateUserAsync(id, actorId);
        if (!result)
            return NotFound();
        return Ok(new { message = "User activated successfully" });
    }
    
    /// <summary>
    /// Gets user sessions
    /// </summary>
    [HttpGet("users/{id}/sessions")]
    [ProducesResponseType(typeof(List<UserSession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserSessions(long id)
    {
        var sessions = await _adminService.GetUserSessionsAsync(id);
        return Ok(sessions);
    }
    
    /// <summary>
    /// Revokes user session
    /// </summary>
    [HttpDelete("users/{userId}/sessions/{sessionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(long userId, long sessionId)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.RevokeSessionAsync(sessionId, actorId);
        if (!result)
            return NotFound();
        return NoContent();
    }
    
    /// <summary>
    /// Revokes all user sessions
    /// </summary>
    [HttpDelete("users/{id}/sessions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeAllSessions(long id)
    {
        var actorId = GetCurrentUserId();
        var count = await _adminService.RevokeAllSessionsAsync(id, actorId);
        return Ok(new { revokedCount = count });
    }
    
    #endregion
    
    #region Groups
    
    /// <summary>
    /// Gets paginated list of groups
    /// </summary>
    [HttpGet("groups")]
    [ProducesResponseType(typeof(PagedResult<AdminGroup>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroups([FromQuery] GroupListRequest request)
    {
        var result = await _adminService.GetGroupsAsync(request);
        return Ok(result);
    }
    
    /// <summary>
    /// Gets group by ID
    /// </summary>
    [HttpGet("groups/{id}")]
    [ProducesResponseType(typeof(AdminGroup), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGroup(long id)
    {
        var group = await _adminService.GetGroupAsync(id);
        if (group == null)
            return NotFound();
        return Ok(group);
    }
    
    /// <summary>
    /// Deletes group
    /// </summary>
    [HttpDelete("groups/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGroup(long id)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.DeleteGroupAsync(id, actorId);
        if (!result)
            return NotFound();
        return NoContent();
    }
    
    /// <summary>
    /// Restricts group
    /// </summary>
    [HttpPost("groups/{id}/restrict")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestrictGroup(long id, [FromBody] RestrictRequest request)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.RestrictGroupAsync(id, request.Reason, actorId);
        if (!result)
            return NotFound();
        return Ok(new { message = "Group restricted successfully" });
    }
    
    #endregion
    
    #region Statistics
    
    /// <summary>
    /// Gets system statistics
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(SystemStatistics), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics()
    {
        var stats = await _adminService.GetStatisticsAsync();
        return Ok(stats);
    }
    
    /// <summary>
    /// Gets user statistics
    /// </summary>
    [HttpGet("statistics/users")]
    [ProducesResponseType(typeof(UserStatistics), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserStatistics()
    {
        var stats = await _adminService.GetUserStatisticsAsync();
        return Ok(stats);
    }
    
    /// <summary>
    /// Gets message statistics
    /// </summary>
    [HttpGet("statistics/messages")]
    [ProducesResponseType(typeof(MessageStatistics), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessageStatistics([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var stats = await _adminService.GetMessageStatisticsAsync(from, to);
        return Ok(stats);
    }
    
    /// <summary>
    /// Gets storage statistics
    /// </summary>
    [HttpGet("statistics/storage")]
    [ProducesResponseType(typeof(StorageStatistics), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStorageStatistics()
    {
        var stats = await _adminService.GetStorageStatisticsAsync();
        return Ok(stats);
    }
    
    /// <summary>
    /// Gets conference statistics
    /// </summary>
    [HttpGet("statistics/conferences")]
    [ProducesResponseType(typeof(ConferenceStatistics), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConferenceStatistics()
    {
        var stats = await _adminService.GetConferenceStatisticsAsync();
        return Ok(stats);
    }
    
    #endregion
    
    #region Audit Log
    
    /// <summary>
    /// Gets audit log entries
    /// </summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(PagedResult<AuditLogEntry>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog([FromQuery] AuditLogRequest request)
    {
        var result = await _adminService.GetAuditLogAsync(request);
        return Ok(result);
    }
    
    #endregion
    
    #region System
    
    /// <summary>
    /// Gets system health status
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HealthStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth()
    {
        var health = await _adminService.GetHealthAsync();
        return Ok(health);
    }
    
    /// <summary>
    /// Gets system settings
    /// </summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(SystemSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _adminService.GetSettingsAsync();
        return Ok(settings);
    }
    
    /// <summary>
    /// Updates system settings
    /// </summary>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(SystemSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings([FromBody] SystemSettings settings)
    {
        var actorId = GetCurrentUserId();
        var result = await _adminService.UpdateSettingsAsync(settings, actorId);
        return Ok(result);
    }
    
    /// <summary>
    /// Clears system cache
    /// </summary>
    [HttpPost("cache/clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearCache()
    {
        await _adminService.ClearCacheAsync();
        return Ok(new { message = "Cache cleared successfully" });
    }
    
    #endregion
    
    #region LDAP
    
    /// <summary>
    /// Tests LDAP connection
    /// </summary>
    [HttpPost("ldap/test")]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestLdapConnection()
    {
        var result = await _ldapService.TestConnectionAsync();
        return Ok(result);
    }
    
    /// <summary>
    /// Syncs users from LDAP
    /// </summary>
    [HttpPost("ldap/sync")]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncLdapUsers()
    {
        var result = await _ldapService.SyncUsersAsync();
        return Ok(result);
    }
    
    /// <summary>
    /// Searches users in LDAP
    /// </summary>
    [HttpGet("ldap/users")]
    [ProducesResponseType(typeof(IEnumerable<LdapUser>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchLdapUsers([FromQuery] string query, [FromQuery] int limit = 50)
    {
        var users = await _ldapService.SearchUsersAsync(query, limit);
        return Ok(users);
    }
    
    /// <summary>
    /// Gets LDAP groups
    /// </summary>
    [HttpGet("ldap/groups")]
    [ProducesResponseType(typeof(IEnumerable<LdapGroup>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLdapGroups()
    {
        var groups = await _ldapService.GetGroupsAsync();
        return Ok(groups);
    }
    
    #endregion
    
    #region Private Methods
    
    private long GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
    
    #endregion
}

/// <summary>
/// Suspend request
/// </summary>
public class SuspendRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Restrict request
/// </summary>
public class RestrictRequest
{
    public string Reason { get; set; } = string.Empty;
}
