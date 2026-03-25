using System.Diagnostics;
using AdminService.Data;
using AdminService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace AdminService.Services;

/// <summary>
/// Admin service for user management, statistics, and system operations
/// </summary>
public interface IAdminService
{
    #region User Management
    
    /// <summary>
    /// Gets paginated list of users
    /// </summary>
    Task<PagedResult<AdminUser>> GetUsersAsync(UserListRequest request);
    
    /// <summary>
    /// Gets user by ID
    /// </summary>
    Task<AdminUser?> GetUserAsync(long userId);
    
    /// <summary>
    /// Creates a new user
    /// </summary>
    Task<AdminUser> CreateUserAsync(CreateUserRequest request, long actorUserId);
    
    /// <summary>
    /// Updates user
    /// </summary>
    Task<AdminUser?> UpdateUserAsync(long userId, UpdateUserRequest request, long actorUserId);
    
    /// <summary>
    /// Deletes user (soft delete)
    /// </summary>
    Task<bool> DeleteUserAsync(long userId, long actorUserId);
    
    /// <summary>
    /// Resets user password
    /// </summary>
    Task<bool> ResetUserPasswordAsync(long userId, ResetPasswordRequest request, long actorUserId);
    
    /// <summary>
    /// Suspends user
    /// </summary>
    Task<bool> SuspendUserAsync(long userId, string reason, long actorUserId);
    
    /// <summary>
    /// Activates user
    /// </summary>
    Task<bool> ActivateUserAsync(long userId, long actorUserId);
    
    /// <summary>
    /// Gets user sessions
    /// </summary>
    Task<List<UserSession>> GetUserSessionsAsync(long userId);
    
    /// <summary>
    /// Revokes user session
    /// </summary>
    Task<bool> RevokeSessionAsync(long sessionId, long actorUserId);
    
    /// <summary>
    /// Revokes all user sessions
    /// </summary>
    Task<int> RevokeAllSessionsAsync(long userId, long actorUserId);
    
    #endregion
    
    #region Group Management
    
    /// <summary>
    /// Gets paginated list of groups
    /// </summary>
    Task<PagedResult<AdminGroup>> GetGroupsAsync(GroupListRequest request);
    
    /// <summary>
    /// Gets group by ID
    /// </summary>
    Task<AdminGroup?> GetGroupAsync(long groupId);
    
    /// <summary>
    /// Deletes group
    /// </summary>
    Task<bool> DeleteGroupAsync(long groupId, long actorUserId);
    
    /// <summary>
    /// Restricts group
    /// </summary>
    Task<bool> RestrictGroupAsync(long groupId, string reason, long actorUserId);
    
    #endregion
    
    #region Statistics
    
    /// <summary>
    /// Gets system statistics
    /// </summary>
    Task<SystemStatistics> GetStatisticsAsync();
    
    /// <summary>
    /// Gets user statistics
    /// </summary>
    Task<UserStatistics> GetUserStatisticsAsync();
    
    /// <summary>
    /// Gets message statistics
    /// </summary>
    Task<MessageStatistics> GetMessageStatisticsAsync(DateTime? from = null, DateTime? to = null);
    
    /// <summary>
    /// Gets storage statistics
    /// </summary>
    Task<StorageStatistics> GetStorageStatisticsAsync();
    
    /// <summary>
    /// Gets conference statistics
    /// </summary>
    Task<ConferenceStatistics> GetConferenceStatisticsAsync();
    
    #endregion
    
    #region Audit Log
    
    /// <summary>
    /// Gets audit log entries
    /// </summary>
    Task<PagedResult<AuditLogEntry>> GetAuditLogAsync(AuditLogRequest request);
    
    /// <summary>
    /// Logs an audit action
    /// </summary>
    Task LogAuditAsync(AuditAction action, string resourceType, long? resourceId, 
        string description, long? actorUserId, string? actorIpAddress = null,
        Dictionary<string, object>? oldValues = null, Dictionary<string, object>? newValues = null);
    
    #endregion
    
    #region System
    
    /// <summary>
    /// Gets system health status
    /// </summary>
    Task<HealthStatus> GetHealthAsync();
    
    /// <summary>
    /// Gets system settings
    /// </summary>
    Task<SystemSettings> GetSettingsAsync();
    
    /// <summary>
    /// Updates system settings
    /// </summary>
    Task<SystemSettings> UpdateSettingsAsync(SystemSettings settings, long actorUserId);
    
    /// <summary>
    /// Clears system cache
    /// </summary>
    Task ClearCacheAsync();
    
    #endregion
}

/// <summary>
/// User session information
/// </summary>
public class UserSession
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Admin service implementation
/// </summary>
public class AdminService : IAdminService
{
    private readonly AdminDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminService> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    public AdminService(
        AdminDbContext dbContext,
        IConnectionMultiplexer redis,
        IMemoryCache cache,
        ILogger<AdminService> logger,
        IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _redis = redis;
        _cache = cache;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    #region User Management
    
    public async Task<PagedResult<AdminUser>> GetUsersAsync(UserListRequest request)
    {
        var query = _dbContext.Users.AsQueryable();
        
        // Apply filters
        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(u => 
                u.Username.ToLower().Contains(search) ||
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.DisplayName != null && u.DisplayName.ToLower().Contains(search)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(search)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(search)));
        }
        
        if (request.Role.HasValue)
        {
            query = query.Where(u => u.Role == (int)request.Role.Value);
        }
        
        if (request.Status.HasValue)
        {
            query = query.Where(u => u.Status == (int)request.Status.Value);
        }
        
        if (!string.IsNullOrEmpty(request.Department))
        {
            query = query.Where(u => u.Department == request.Department);
        }
        
        if (request.CreatedFrom.HasValue)
        {
            query = query.Where(u => u.CreatedAt >= request.CreatedFrom.Value);
        }
        
        if (request.CreatedTo.HasValue)
        {
            query = query.Where(u => u.CreatedAt <= request.CreatedTo.Value);
        }
        
        // Get total count
        var totalCount = await query.CountAsync();
        
        // Apply sorting
        query = request.SortBy.ToLower() switch
        {
            "username" => request.SortDescending ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
            "email" => request.SortDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "lastlogin" => request.SortDescending ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderBy(u => u.LastLoginAt),
            _ => request.SortDescending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt)
        };
        
        // Apply pagination
        var users = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new AdminUser
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber,
                FirstName = u.FirstName,
                LastName = u.LastName,
                DisplayName = u.DisplayName,
                Role = (UserRole)u.Role,
                Status = (UserStatus)u.Status,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                TwoFactorEnabled = u.TwoFactorEnabled,
                Department = u.Department,
                LdapDn = u.LdapDn
            })
            .ToListAsync();
        
        // Get session counts
        var userIds = users.Select(u => u.Id).ToList();
        var sessionCounts = await _dbContext.Sessions
            .Where(s => userIds.Contains(s.UserId) && s.IsActive)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);
        
        foreach (var user in users)
        {
            user.SessionCount = sessionCounts.GetValueOrDefault(user.Id, 0);
        }
        
        return new PagedResult<AdminUser>
        {
            Items = users,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
    
    public async Task<AdminUser?> GetUserAsync(long userId)
    {
        var user = await _dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => new AdminUser
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber,
                FirstName = u.FirstName,
                LastName = u.LastName,
                DisplayName = u.DisplayName,
                Role = (UserRole)u.Role,
                Status = (UserStatus)u.Status,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                TwoFactorEnabled = u.TwoFactorEnabled,
                Department = u.Department,
                LdapDn = u.LdapDn
            })
            .FirstOrDefaultAsync();
        
        if (user != null)
        {
            user.SessionCount = await _dbContext.Sessions
                .CountAsync(s => s.UserId == userId && s.IsActive);
        }
        
        return user;
    }
    
    public async Task<AdminUser> CreateUserAsync(CreateUserRequest request, long actorUserId)
    {
        // Check if username exists
        if (await _dbContext.Users.AnyAsync(u => u.Username == request.Username))
        {
            throw new InvalidOperationException($"Username '{request.Username}' already exists");
        }
        
        // Check if email exists
        if (await _dbContext.Users.AnyAsync(u => u.Email == request.Email))
        {
            throw new InvalidOperationException($"Email '{request.Email}' already exists");
        }
        
        var user = new DbUser
        {
            Username = request.Username,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName ?? 
                (!string.IsNullOrEmpty(request.FirstName) || !string.IsNullOrEmpty(request.LastName)
                    ? $"{request.FirstName} {request.LastName}".Trim()
                    : request.Username),
            Role = (int)request.Role,
            Status = (int)UserStatus.Active,
            Department = request.Department,
            PasswordHash = HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };
        
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.UserCreated, "User", user.Id, 
            $"Created user '{user.Username}'", actorUserId);
        
        _logger.LogInformation("User {Username} created by admin {ActorId}", 
            user.Username, actorUserId);
        
        return await GetUserAsync(user.Id) ?? throw new InvalidOperationException("Failed to get created user");
    }
    
    public async Task<AdminUser?> UpdateUserAsync(long userId, UpdateUserRequest request, long actorUserId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return null;
        
        var oldValues = new Dictionary<string, object>();
        var newValues = new Dictionary<string, object>();
        
        if (request.FirstName != null && user.FirstName != request.FirstName)
        {
            oldValues["FirstName"] = user.FirstName ?? "";
            user.FirstName = request.FirstName;
            newValues["FirstName"] = request.FirstName;
        }
        
        if (request.LastName != null && user.LastName != request.LastName)
        {
            oldValues["LastName"] = user.LastName ?? "";
            user.LastName = request.LastName;
            newValues["LastName"] = request.LastName;
        }
        
        if (request.DisplayName != null && user.DisplayName != request.DisplayName)
        {
            oldValues["DisplayName"] = user.DisplayName ?? "";
            user.DisplayName = request.DisplayName;
            newValues["DisplayName"] = request.DisplayName;
        }
        
        if (request.Role.HasValue && user.Role != (int)request.Role.Value)
        {
            oldValues["Role"] = (UserRole)user.Role;
            user.Role = (int)request.Role.Value;
            newValues["Role"] = request.Role.Value;
        }
        
        if (request.Status.HasValue && user.Status != (int)request.Status.Value)
        {
            oldValues["Status"] = (UserStatus)user.Status;
            user.Status = (int)request.Status.Value;
            newValues["Status"] = request.Status.Value;
        }
        
        if (request.Department != null && user.Department != request.Department)
        {
            oldValues["Department"] = user.Department ?? "";
            user.Department = request.Department;
            newValues["Department"] = request.Department;
        }
        
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        
        if (newValues.Count > 0)
        {
            await LogAuditAsync(AuditAction.UserUpdated, "User", user.Id,
                $"Updated user '{user.Username}'", actorUserId,
                oldValues: oldValues, newValues: newValues);
        }
        
        return await GetUserAsync(userId);
    }
    
    public async Task<bool> DeleteUserAsync(long userId, long actorUserId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return false;
        
        // Soft delete
        user.Status = (int)UserStatus.Deleted;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        
        // Revoke all sessions
        await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.UserDeleted, "User", user.Id,
            $"Deleted user '{user.Username}'", actorUserId);
        
        _logger.LogInformation("User {Username} deleted by admin {ActorId}", 
            user.Username, actorUserId);
        
        return true;
    }
    
    public async Task<bool> ResetUserPasswordAsync(long userId, ResetPasswordRequest request, long actorUserId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return false;
        
        user.PasswordHash = HashPassword(request.NewPassword);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.RequirePasswordChange = request.RequireChangeOnNextLogin;
        user.UpdatedAt = DateTime.UtcNow;
        
        // Revoke all sessions
        await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.UserPasswordReset, "User", user.Id,
            $"Reset password for user '{user.Username}'", actorUserId);
        
        _logger.LogInformation("Password reset for user {Username} by admin {ActorId}", 
            user.Username, actorUserId);
        
        return true;
    }
    
    public async Task<bool> SuspendUserAsync(long userId, string reason, long actorUserId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return false;
        
        user.Status = (int)UserStatus.Suspended;
        user.SuspendedAt = DateTime.UtcNow;
        user.SuspensionReason = reason;
        user.UpdatedAt = DateTime.UtcNow;
        
        // Revoke all sessions
        await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.UserSuspended, "User", user.Id,
            $"Suspended user '{user.Username}': {reason}", actorUserId);
        
        _logger.LogInformation("User {Username} suspended by admin {ActorId}: {Reason}", 
            user.Username, actorUserId, reason);
        
        return true;
    }
    
    public async Task<bool> ActivateUserAsync(long userId, long actorUserId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return false;
        
        user.Status = (int)UserStatus.Active;
        user.SuspendedAt = null;
        user.SuspensionReason = null;
        user.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.UserActivated, "User", user.Id,
            $"Activated user '{user.Username}'", actorUserId);
        
        _logger.LogInformation("User {Username} activated by admin {ActorId}", 
            user.Username, actorUserId);
        
        return true;
    }
    
    public async Task<List<UserSession>> GetUserSessionsAsync(long userId)
    {
        return await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LastActiveAt)
            .Select(s => new UserSession
            {
                Id = s.Id,
                UserId = s.UserId,
                DeviceName = s.DeviceName,
                DeviceType = s.DeviceType,
                IpAddress = s.IpAddress,
                Location = s.Location,
                CreatedAt = s.CreatedAt,
                LastActiveAt = s.LastActiveAt,
                IsActive = s.IsActive
            })
            .ToListAsync();
    }
    
    public async Task<bool> RevokeSessionAsync(long sessionId, long actorUserId)
    {
        var session = await _dbContext.Sessions.FindAsync(sessionId);
        if (session == null)
            return false;
        
        session.IsActive = false;
        session.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.SessionRevoked, "Session", session.Id,
            $"Revoked session for user {session.UserId}", actorUserId);
        
        return true;
    }
    
    public async Task<int> RevokeAllSessionsAsync(long userId, long actorUserId)
    {
        var count = await _dbContext.Sessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        
        await LogAuditAsync(AuditAction.SessionRevoked, "User", userId,
            $"Revoked all sessions for user {userId}", actorUserId);
        
        return count;
    }
    
    #endregion
    
    #region Group Management
    
    public async Task<PagedResult<AdminGroup>> GetGroupsAsync(GroupListRequest request)
    {
        var query = _dbContext.Groups.AsQueryable();
        
        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(g => 
                g.Title.ToLower().Contains(search) ||
                (g.Username != null && g.Username.ToLower().Contains(search)));
        }
        
        if (request.Type.HasValue)
        {
            query = query.Where(g => g.Type == (int)request.Type.Value);
        }
        
        if (request.CreatedFrom.HasValue)
        {
            query = query.Where(g => g.CreatedAt >= request.CreatedFrom.Value);
        }
        
        if (request.CreatedTo.HasValue)
        {
            query = query.Where(g => g.CreatedAt <= request.CreatedTo.Value);
        }
        
        var totalCount = await query.CountAsync();
        
        query = request.SortBy.ToLower() switch
        {
            "title" => request.SortDescending ? query.OrderByDescending(g => g.Title) : query.OrderBy(g => g.Title),
            "members" => request.SortDescending ? query.OrderByDescending(g => g.MemberCount) : query.OrderBy(g => g.MemberCount),
            _ => request.SortDescending ? query.OrderByDescending(g => g.CreatedAt) : query.OrderBy(g => g.CreatedAt)
        };
        
        var groups = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(g => new AdminGroup
            {
                Id = g.Id,
                Title = g.Title,
                Description = g.Description,
                Username = g.Username,
                Type = (GroupType)g.Type,
                OwnerId = g.OwnerId,
                OwnerName = _dbContext.Users
                    .Where(u => u.Id == g.OwnerId)
                    .Select(u => u.DisplayName ?? u.Username)
                    .FirstOrDefault() ?? "",
                MemberCount = g.MemberCount,
                CreatedAt = g.CreatedAt,
                IsVerified = g.IsVerified,
                IsRestricted = g.IsRestricted
            })
            .ToListAsync();
        
        return new PagedResult<AdminGroup>
        {
            Items = groups,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
    
    public async Task<AdminGroup?> GetGroupAsync(long groupId)
    {
        return await _dbContext.Groups
            .Where(g => g.Id == groupId)
            .Select(g => new AdminGroup
            {
                Id = g.Id,
                Title = g.Title,
                Description = g.Description,
                Username = g.Username,
                Type = (GroupType)g.Type,
                OwnerId = g.OwnerId,
                OwnerName = _dbContext.Users
                    .Where(u => u.Id == g.OwnerId)
                    .Select(u => u.DisplayName ?? u.Username)
                    .FirstOrDefault() ?? "",
                MemberCount = g.MemberCount,
                CreatedAt = g.CreatedAt,
                IsVerified = g.IsVerified,
                IsRestricted = g.IsRestricted
            })
            .FirstOrDefaultAsync();
    }
    
    public async Task<bool> DeleteGroupAsync(long groupId, long actorUserId)
    {
        var group = await _dbContext.Groups.FindAsync(groupId);
        if (group == null)
            return false;
        
        _dbContext.Groups.Remove(group);
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.GroupDeleted, "Group", group.Id,
            $"Deleted group '{group.Title}'", actorUserId);
        
        _logger.LogInformation("Group {Title} deleted by admin {ActorId}", 
            group.Title, actorUserId);
        
        return true;
    }
    
    public async Task<bool> RestrictGroupAsync(long groupId, string reason, long actorUserId)
    {
        var group = await _dbContext.Groups.FindAsync(groupId);
        if (group == null)
            return false;
        
        group.IsRestricted = true;
        group.RestrictionReason = reason;
        group.UpdatedAt = DateTime.UtcNow;
        
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.GroupRestricted, "Group", group.Id,
            $"Restricted group '{group.Title}': {reason}", actorUserId);
        
        _logger.LogInformation("Group {Title} restricted by admin {ActorId}: {Reason}", 
            group.Title, actorUserId, reason);
        
        return true;
    }
    
    #endregion
    
    #region Statistics
    
    public async Task<SystemStatistics> GetStatisticsAsync()
    {
        var cacheKey = "admin:statistics";
        if (_cache.TryGetValue(cacheKey, out SystemStatistics? cached))
        {
            return cached!;
        }
        
        var stats = new SystemStatistics
        {
            Users = await GetUserStatisticsAsync(),
            Messages = await GetMessageStatisticsAsync(),
            Groups = await GetGroupStatisticsAsync(),
            Storage = await GetStorageStatisticsAsync(),
            Conferences = await GetConferenceStatisticsAsync(),
            Server = await GetServerStatisticsAsync()
        };
        
        _cache.Set(cacheKey, stats, TimeSpan.FromMinutes(1));
        return stats;
    }
    
    public async Task<UserStatistics> GetUserStatisticsAsync()
    {
        var now = DateTime.UtcNow;
        var today = DateTime.Today;
        var weekAgo = today.AddDays(-7);
        var monthAgo = today.AddMonths(-1);
        
        var totalUsers = await _dbContext.Users.CountAsync();
        var activeToday = await _dbContext.Users
            .CountAsync(u => u.LastLoginAt >= today);
        var activeWeek = await _dbContext.Users
            .CountAsync(u => u.LastLoginAt >= weekAgo);
        var activeMonth = await _dbContext.Users
            .CountAsync(u => u.LastLoginAt >= monthAgo);
        var newToday = await _dbContext.Users
            .CountAsync(u => u.CreatedAt >= today);
        var newWeek = await _dbContext.Users
            .CountAsync(u => u.CreatedAt >= weekAgo);
        var newMonth = await _dbContext.Users
            .CountAsync(u => u.CreatedAt >= monthAgo);
        
        // Get online count from Redis
        var onlineCount = await GetOnlineCountAsync();
        
        // Users by department
        var byDepartment = await _dbContext.Users
            .Where(u => u.Department != null)
            .GroupBy(u => u.Department!)
            .Select(g => new { Department = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Department, x => x.Count);
        
        // Registration trend (last 30 days)
        var trend = await GetRegistrationTrendAsync(30);
        
        return new UserStatistics
        {
            TotalUsers = totalUsers,
            ActiveUsersToday = activeToday,
            ActiveUsersWeek = activeWeek,
            ActiveUsersMonth = activeMonth,
            NewUsersToday = newToday,
            NewUsersWeek = newWeek,
            NewUsersMonth = newMonth,
            OnlineNow = onlineCount,
            UsersByDepartment = byDepartment,
            RegistrationTrend = trend
        };
    }
    
    public async Task<MessageStatistics> GetMessageStatisticsAsync(DateTime? from = null, DateTime? to = null)
    {
        var today = DateTime.Today;
        var weekAgo = today.AddDays(-7);
        var monthAgo = today.AddMonths(-1);
        
        var totalMessages = await _dbContext.Messages.CountAsync();
        var messagesToday = await _dbContext.Messages
            .CountAsync(m => m.CreatedAt >= today);
        var messagesWeek = await _dbContext.Messages
            .CountAsync(m => m.CreatedAt >= weekAgo);
        var messagesMonth = await _dbContext.Messages
            .CountAsync(m => m.CreatedAt >= monthAgo);
        
        var totalUsers = await _dbContext.Users.CountAsync();
        
        // Message trend
        var trend = await GetMessageTrendAsync(30);
        
        return new MessageStatistics
        {
            TotalMessages = totalMessages,
            MessagesToday = messagesToday,
            MessagesWeek = messagesWeek,
            MessagesMonth = messagesMonth,
            AverageMessagesPerUser = totalUsers > 0 ? (double)totalMessages / totalUsers : 0,
            MessageTrend = trend
        };
    }
    
    public async Task<GroupStatistics> GetGroupStatisticsAsync()
    {
        var today = DateTime.Today;
        var weekAgo = today.AddDays(-7);
        var monthAgo = today.AddMonths(-1);
        
        var totalGroups = await _dbContext.Groups.CountAsync();
        var activeWeek = await _dbContext.Groups
            .CountAsync(g => g.LastMessageAt >= weekAgo);
        var activeMonth = await _dbContext.Groups
            .CountAsync(g => g.LastMessageAt >= monthAgo);
        var newToday = await _dbContext.Groups
            .CountAsync(g => g.CreatedAt >= today);
        
        var avgMembers = await _dbContext.Groups.AverageAsync(g => g.MemberCount);
        
        var byType = await _dbContext.Groups
            .GroupBy(g => g.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(x => x.Type, x => x.Count);
        
        return new GroupStatistics
        {
            TotalGroups = totalGroups,
            ActiveGroupsWeek = activeWeek,
            ActiveGroupsMonth = activeMonth,
            NewGroupsToday = newToday,
            AverageMembersPerGroup = avgMembers,
            GroupsByType = byType
        };
    }
    
    public async Task<StorageStatistics> GetStorageStatisticsAsync()
    {
        var today = DateTime.Today;
        
        var totalFiles = await _dbContext.Files.CountAsync();
        var totalSize = await _dbContext.Files.SumAsync(f => f.SizeBytes);
        var filesToday = await _dbContext.Files.CountAsync(f => f.CreatedAt >= today);
        var sizeToday = await _dbContext.Files
            .Where(f => f.CreatedAt >= today)
            .SumAsync(f => f.SizeBytes);
        
        var byType = await _dbContext.Files
            .GroupBy(f => f.ContentType.StartsWith("image") ? "image" :
                         f.ContentType.StartsWith("video") ? "video" :
                         f.ContentType.StartsWith("audio") ? "audio" : "other")
            .Select(g => new { Type = g.Key, Count = g.Count(), Size = g.Sum(f => f.SizeBytes) })
            .ToListAsync();
        
        return new StorageStatistics
        {
            TotalFiles = totalFiles,
            TotalSizeBytes = totalSize,
            FilesUploadedToday = filesToday,
            SizeUploadedTodayBytes = sizeToday,
            FilesByType = byType.ToDictionary(x => x.Type, x => (long)x.Count),
            SizeByType = byType.ToDictionary(x => x.Type, x => x.Size)
        };
    }
    
    public async Task<ConferenceStatistics> GetConferenceStatisticsAsync()
    {
        var today = DateTime.Today;
        
        var activeNow = await _dbContext.Conferences.CountAsync(c => c.EndedAt == null);
        var totalParticipants = await _dbContext.Conferences
            .Where(c => c.EndedAt == null)
            .SumAsync(c => c.ParticipantCount);
        var totalToday = await _dbContext.Conferences
            .CountAsync(c => c.StartedAt >= today);
        var totalMinutesToday = await _dbContext.Conferences
            .Where(c => c.StartedAt >= today && c.EndedAt.HasValue)
            .SumAsync(c => EF.Functions.DateDiffMinute(c.StartedAt, c.EndedAt!.Value));
        
        var avgParticipants = await _dbContext.Conferences
            .Where(c => c.EndedAt.HasValue)
            .AverageAsync(c => (double?)c.ParticipantCount) ?? 0;
        
        var avgDuration = await _dbContext.Conferences
            .Where(c => c.EndedAt.HasValue)
            .AverageAsync(c => (double?)EF.Functions.DateDiffMinute(c.StartedAt, c.EndedAt!.Value)) ?? 0;
        
        return new ConferenceStatistics
        {
            ActiveConferences = activeNow,
            TotalParticipants = totalParticipants,
            TotalConferencesToday = totalToday,
            TotalMinutesToday = totalMinutesToday,
            AverageParticipantsPerConference = avgParticipants,
            AverageDurationMinutes = avgDuration
        };
    }
    
    private async Task<ServerStatistics> GetServerStatisticsAsync()
    {
        var process = Process.GetCurrentProcess();
        var startTime = process.StartTime;
        
        // Get CPU and memory usage
        var cpuUsage = await GetCpuUsageAsync();
        var memoryUsed = process.WorkingSet64;
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        
        // Get disk usage
        var drive = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/");
        var diskUsed = drive.TotalSize - drive.AvailableFreeSpace;
        
        // Get connection counts from Redis
        var activeConnections = await GetActiveConnectionCountAsync();
        var activeWebSockets = await GetActiveWebSocketCountAsync();
        
        return new ServerStatistics
        {
            CpuUsagePercent = cpuUsage,
            MemoryUsagePercent = (double)memoryUsed / totalMemory * 100,
            MemoryUsedBytes = memoryUsed,
            MemoryTotalBytes = totalMemory,
            DiskUsagePercent = (double)diskUsed / drive.TotalSize * 100,
            DiskUsedBytes = diskUsed,
            DiskTotalBytes = drive.TotalSize,
            ActiveConnections = activeConnections,
            ActiveWebSockets = activeWebSockets,
            Uptime = DateTime.UtcNow - startTime,
            StartTime = startTime,
            Version = typeof(AdminService).Assembly.GetName().Version?.ToString() ?? "1.0.0"
        };
    }
    
    #endregion
    
    #region Audit Log
    
    public async Task<PagedResult<AuditLogEntry>> GetAuditLogAsync(AuditLogRequest request)
    {
        var query = _dbContext.AuditLog.AsQueryable();
        
        if (request.ActorUserId.HasValue)
        {
            query = query.Where(a => a.ActorUserId == request.ActorUserId);
        }
        
        if (request.Action.HasValue)
        {
            query = query.Where(a => a.Action == (int)request.Action.Value);
        }
        
        if (!string.IsNullOrEmpty(request.ResourceType))
        {
            query = query.Where(a => a.ResourceType == request.ResourceType);
        }
        
        if (request.ResourceId.HasValue)
        {
            query = query.Where(a => a.ResourceId == request.ResourceId);
        }
        
        if (request.From.HasValue)
        {
            query = query.Where(a => a.Timestamp >= request.From.Value);
        }
        
        if (request.To.HasValue)
        {
            query = query.Where(a => a.Timestamp <= request.To.Value);
        }
        
        if (request.Success.HasValue)
        {
            query = query.Where(a => a.Success == request.Success.Value);
        }
        
        var totalCount = await query.CountAsync();
        
        query = request.SortDescending 
            ? query.OrderByDescending(a => a.Timestamp) 
            : query.OrderBy(a => a.Timestamp);
        
        var entries = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AuditLogEntry
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                ActorUserId = a.ActorUserId,
                ActorUsername = a.ActorUsername,
                ActorIpAddress = a.ActorIpAddress,
                Action = (AuditAction)a.Action,
                ResourceType = a.ResourceType,
                ResourceId = a.ResourceId,
                Description = a.Description,
                OldValues = a.OldValues != null 
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(a.OldValues) 
                    : null,
                NewValues = a.NewValues != null 
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(a.NewValues) 
                    : null,
                Success = a.Success,
                ErrorMessage = a.ErrorMessage
            })
            .ToListAsync();
        
        return new PagedResult<AuditLogEntry>
        {
            Items = entries,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
    
    public async Task LogAuditAsync(AuditAction action, string resourceType, long? resourceId,
        string description, long? actorUserId, string? actorIpAddress = null,
        Dictionary<string, object>? oldValues = null, Dictionary<string, object>? newValues = null)
    {
        string? actorUsername = null;
        if (actorUserId.HasValue)
        {
            actorUsername = await _dbContext.Users
                .Where(u => u.Id == actorUserId.Value)
                .Select(u => u.Username)
                .FirstOrDefaultAsync();
        }
        
        var entry = new DbAuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUserId = actorUserId,
            ActorUsername = actorUsername,
            ActorIpAddress = actorIpAddress,
            Action = (int)action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Description = description,
            OldValues = oldValues != null ? System.Text.Json.JsonSerializer.Serialize(oldValues) : null,
            NewValues = newValues != null ? System.Text.Json.JsonSerializer.Serialize(newValues) : null,
            Success = true
        };
        
        _dbContext.AuditLog.Add(entry);
        await _dbContext.SaveChangesAsync();
    }
    
    #endregion
    
    #region System
    
    public async Task<HealthStatus> GetHealthAsync()
    {
        var status = new HealthStatus
        {
            Status = "healthy",
            CheckedAt = DateTime.UtcNow,
            Components = new List<ComponentHealth>()
        };
        
        // Check database
        var dbHealth = await CheckDatabaseHealthAsync();
        status.Components.Add(dbHealth);
        if (dbHealth.Status != "healthy")
            status.Status = "degraded";
        
        // Check Redis
        var redisHealth = await CheckRedisHealthAsync();
        status.Components.Add(redisHealth);
        if (redisHealth.Status != "healthy" && status.Status == "healthy")
            status.Status = "degraded";
        
        // Check storage
        var storageHealth = await CheckStorageHealthAsync();
        status.Components.Add(storageHealth);
        
        return status;
    }
    
    public async Task<SystemSettings> GetSettingsAsync()
    {
        var settings = await _dbContext.SystemSettings
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync();
        
        if (settings == null)
        {
            return new SystemSettings();
        }
        
        return System.Text.Json.JsonSerializer.Deserialize<SystemSettings>(settings.SettingsJson) 
            ?? new SystemSettings();
    }
    
    public async Task<SystemSettings> UpdateSettingsAsync(SystemSettings settings, long actorUserId)
    {
        var current = await _dbContext.SystemSettings
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync();
        
        var newSettings = new DbSystemSettings
        {
            Version = (current?.Version ?? 0) + 1,
            SettingsJson = System.Text.Json.JsonSerializer.Serialize(settings),
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = actorUserId
        };
        
        _dbContext.SystemSettings.Add(newSettings);
        await _dbContext.SaveChangesAsync();
        
        await LogAuditAsync(AuditAction.SystemConfigChanged, "SystemSettings", newSettings.Id,
            "System settings updated", actorUserId);
        
        // Clear cache
        _cache.Remove("admin:settings");
        
        return settings;
    }
    
    public async Task ClearCacheAsync()
    {
        // Clear memory cache
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
        }
        
        // Clear Redis cache
        var endpoints = _redis.GetEndPoints();
        foreach (var endpoint in endpoints)
        {
            var server = _redis.GetServer(endpoint);
            await server.FlushDatabaseAsync();
        }
        
        _logger.LogInformation("System cache cleared");
    }
    
    #endregion
    
    #region Private Helpers
    
    private static string HashPassword(string password)
    {
        // Use BCrypt or similar in production
        return BCrypt.Net.BCrypt.HashPassword(password);
    }
    
    private async Task<int> GetOnlineCountAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var keys = await db.SetMembersAsync("online:users");
            return keys.Length;
        }
        catch
        {
            return 0;
        }
    }
    
    private async Task<int> GetActiveConnectionCountAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var count = await db.StringGetAsync("stats:connections");
            return (int?)count ?? 0;
        }
        catch
        {
            return 0;
        }
    }
    
    private async Task<int> GetActiveWebSocketCountAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var count = await db.StringGetAsync("stats:websockets");
            return (int?)count ?? 0;
        }
        catch
        {
            return 0;
        }
    }
    
    private async Task<double> GetCpuUsageAsync()
    {
        var startTime = DateTime.UtcNow;
        var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
        
        await Task.Delay(500);
        
        var endTime = DateTime.UtcNow;
        var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
        
        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMsPassed = (endTime - startTime).TotalMilliseconds;
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
        
        return cpuUsageTotal * 100;
    }
    
    private async Task<List<TimeSeriesPoint>> GetRegistrationTrendAsync(int days)
    {
        var startDate = DateTime.Today.AddDays(-days);
        
        var registrations = await _dbContext.Users
            .Where(u => u.CreatedAt >= startDate)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);
        
        var result = new List<TimeSeriesPoint>();
        for (var date = startDate; date <= DateTime.Today; date = date.AddDays(1))
        {
            result.Add(new TimeSeriesPoint
            {
                Timestamp = date,
                Value = registrations.GetValueOrDefault(date, 0)
            });
        }
        
        return result;
    }
    
    private async Task<List<TimeSeriesPoint>> GetMessageTrendAsync(int days)
    {
        var startDate = DateTime.Today.AddDays(-days);
        
        var messages = await _dbContext.Messages
            .Where(m => m.CreatedAt >= startDate)
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);
        
        var result = new List<TimeSeriesPoint>();
        for (var date = startDate; date <= DateTime.Today; date = date.AddDays(1))
        {
            result.Add(new TimeSeriesPoint
            {
                Timestamp = date,
                Value = messages.GetValueOrDefault(date, 0)
            });
        }
        
        return result;
    }
    
    private async Task<ComponentHealth> CheckDatabaseHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync();
            sw.Stop();
            
            return new ComponentHealth
            {
                Name = "Database",
                Status = canConnect ? "healthy" : "unhealthy",
                ResponseTime = sw.Elapsed,
                Message = canConnect ? null : "Cannot connect to database"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ComponentHealth
            {
                Name = "Database",
                Status = "unhealthy",
                ResponseTime = sw.Elapsed,
                Message = ex.Message
            };
        }
    }
    
    private async Task<ComponentHealth> CheckRedisHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            sw.Stop();
            
            return new ComponentHealth
            {
                Name = "Redis",
                Status = "healthy",
                ResponseTime = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ComponentHealth
            {
                Name = "Redis",
                Status = "unhealthy",
                ResponseTime = sw.Elapsed,
                Message = ex.Message
            };
        }
    }
    
    private async Task<ComponentHealth> CheckStorageHealthAsync()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/");
            var usedPercent = (double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100;
            
            return new ComponentHealth
            {
                Name = "Storage",
                Status = usedPercent > 90 ? "unhealthy" : usedPercent > 80 ? "degraded" : "healthy",
                Details = new Dictionary<string, object>
                {
                    ["usedPercent"] = usedPercent,
                    ["availableGB"] = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024)
                }
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth
            {
                Name = "Storage",
                Status = "unhealthy",
                Message = ex.Message
            };
        }
    }
    
    #endregion
}
