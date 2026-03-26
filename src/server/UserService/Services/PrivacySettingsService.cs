using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserService.Data;
using UserService.Models;

namespace UserService.Services;

/// <summary>
/// Service for managing user privacy settings
/// </summary>
public interface IPrivacySettingsService
{
    Task<PrivacySettings> GetPrivacySettingsAsync(Guid userId);
    Task<PrivacySettings> UpdatePrivacySettingsAsync(Guid userId, UpdatePrivacySettingsRequest request);
    Task<bool> SetLastSeenVisibilityAsync(Guid userId, LastSeenVisibility visibility);
    Task<bool> SetProfilePhotoVisibilityAsync(Guid userId, ProfilePhotoVisibility visibility);
    Task<bool> SetPhoneVisibilityAsync(Guid userId, PhoneVisibility visibility);
    Task<bool> SetGroupsInCommonVisibilityAsync(Guid userId, bool visible);
    Task<bool> SetVoiceCallPrivacyAsync(Guid userId, VoiceCallPrivacy privacy);
    Task<bool> SetInvitePrivilegesAsync(Guid userId, InvitePrivileges privileges);
    Task<bool> BlockUserAsync(Guid userId, Guid userToBlock);
    Task<bool> UnblockUserAsync(Guid userId, Guid userToUnblock);
    Task<List<Guid>> GetBlockedUsersAsync(Guid userId);
    Task<bool> AddToPrivacyExceptionsAsync(Guid userId, PrivacyExceptionType type, Guid exceptionUserId);
    Task<bool> RemoveFromPrivacyExceptionsAsync(Guid userId, PrivacyExceptionType type, Guid exceptionUserId);
    Task<bool> IsUserBlockedAsync(Guid userId, Guid targetUserId);
    Task<bool> CanSendMessageAsync(Guid senderId, Guid recipientId);
    Task<bool> CanSeeLastSeenAsync(Guid viewerId, Guid targetUserId);
    Task<bool> CanSeeProfilePhotoAsync(Guid viewerId, Guid targetUserId);
    Task<bool> CanCallAsync(Guid callerId, Guid calleeId);
}

/// <summary>
/// User privacy settings
/// </summary>
public class PrivacySettings
{
    public Guid UserId { get; set; }
    public LastSeenVisibility LastSeenVisibility { get; set; } = LastSeenVisibility.Everybody;
    public ProfilePhotoVisibility ProfilePhotoVisibility { get; set; } = ProfilePhotoVisibility.Everybody;
    public PhoneVisibility PhoneVisibility { get; set; } = PhoneVisibility.Contacts;
    public bool GroupsInCommonVisible { get; set; } = true;
    public VoiceCallPrivacy VoiceCallPrivacy { get; set; } = VoiceCallPrivacy.Everybody;
    public InvitePrivileges InvitePrivileges { get; set; } = InvitePrivileges.Everybody;
    public bool ForwardingToEveryone { get; set; } = false;
    public bool ShowBioToEveryone { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request to update privacy settings
/// </summary>
public class UpdatePrivacySettingsRequest
{
    public LastSeenVisibility? LastSeenVisibility { get; set; }
    public ProfilePhotoVisibility? ProfilePhotoVisibility { get; set; }
    public PhoneVisibility? PhoneVisibility { get; set; }
    public bool? GroupsInCommonVisible { get; set; }
    public VoiceCallPrivacy? VoiceCallPrivacy { get; set; }
    public InvitePrivileges? InvitePrivileges { get; set; }
    public bool? ForwardingToEveryone { get; set; }
    public bool? ShowBioToEveryone { get; set; }
}

/// <summary>
/// Last seen visibility options
/// </summary>
public enum LastSeenVisibility
{
    Nobody = 0,
    Contacts = 1,
    Everybody = 2
}

/// <summary>
/// Profile photo visibility options
/// </summary>
public enum ProfilePhotoVisibility
{
    Nobody = 0,
    Contacts = 1,
    Everybody = 2
}

/// <summary>
/// Phone number visibility options
/// </summary>
public enum PhoneVisibility
{
    Nobody = 0,
    Contacts = 1,
    Everybody = 2
}

/// <summary>
/// Voice call privacy options
/// </summary>
public enum VoiceCallPrivacy
{
    Nobody = 0,
    Contacts = 1,
    Everybody = 2
}

/// <summary>
/// Invite privileges options
/// </summary>
public enum InvitePrivileges
{
    Nobody = 0,
    Contacts = 1,
    Everybody = 2
}

/// <summary>
/// Privacy exception types
/// </summary>
public enum PrivacyExceptionType
{
    LastSeen,
    ProfilePhoto,
    Phone,
    VoiceCall,
    Invite,
    Forwarding
}

/// <summary>
/// Privacy exception entry
/// </summary>
public class PrivacyException
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public PrivacyExceptionType Type { get; set; }
    public Guid ExceptionUserId { get; set; }
    public bool IsAllowed { get; set; } // true = allow, false = disallow
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Blocked user entry
/// </summary>
public class BlockedUser
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTime BlockedAt { get; set; }
}

/// <summary>
/// Implementation of privacy settings service
/// </summary>
public class PrivacySettingsService : IPrivacySettingsService
{
    private readonly UserDbContext _context;
    private readonly ILogger<PrivacySettingsService> _logger;

    public PrivacySettingsService(UserDbContext context, ILogger<PrivacySettingsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PrivacySettings> GetPrivacySettingsAsync(Guid userId)
    {
        var settings = await _context.PrivacySettings
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (settings == null)
        {
            // Create default settings
            settings = new PrivacySettings
            {
                UserId = userId,
                LastSeenVisibility = LastSeenVisibility.Everybody,
                ProfilePhotoVisibility = ProfilePhotoVisibility.Everybody,
                PhoneVisibility = PhoneVisibility.Contacts,
                GroupsInCommonVisible = true,
                VoiceCallPrivacy = VoiceCallPrivacy.Everybody,
                InvitePrivileges = InvitePrivileges.Everybody,
                ForwardingToEveryone = false,
                ShowBioToEveryone = true,
                UpdatedAt = DateTime.UtcNow
            };

            _context.PrivacySettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        return settings;
    }

    /// <inheritdoc />
    public async Task<PrivacySettings> UpdatePrivacySettingsAsync(Guid userId, UpdatePrivacySettingsRequest request)
    {
        var settings = await GetPrivacySettingsAsync(userId);

        if (request.LastSeenVisibility.HasValue)
            settings.LastSeenVisibility = request.LastSeenVisibility.Value;

        if (request.ProfilePhotoVisibility.HasValue)
            settings.ProfilePhotoVisibility = request.ProfilePhotoVisibility.Value;

        if (request.PhoneVisibility.HasValue)
            settings.PhoneVisibility = request.PhoneVisibility.Value;

        if (request.GroupsInCommonVisible.HasValue)
            settings.GroupsInCommonVisible = request.GroupsInCommonVisible.Value;

        if (request.VoiceCallPrivacy.HasValue)
            settings.VoiceCallPrivacy = request.VoiceCallPrivacy.Value;

        if (request.InvitePrivileges.HasValue)
            settings.InvitePrivileges = request.InvitePrivileges.Value;

        if (request.ForwardingToEveryone.HasValue)
            settings.ForwardingToEveryone = request.ForwardingToEveryone.Value;

        if (request.ShowBioToEveryone.HasValue)
            settings.ShowBioToEveryone = request.ShowBioToEveryone.Value;

        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated privacy settings for user {UserId}", userId);

        return settings;
    }

    /// <inheritdoc />
    public async Task<bool> SetLastSeenVisibilityAsync(Guid userId, LastSeenVisibility visibility)
    {
        var settings = await GetPrivacySettingsAsync(userId);
        settings.LastSeenVisibility = visibility;
        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set last seen visibility to {Visibility} for user {UserId}", visibility, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetProfilePhotoVisibilityAsync(Guid userId, ProfilePhotoVisibility visibility)
    {
        var settings = await GetPrivacySettingsAsync(userId);
        settings.ProfilePhotoVisibility = visibility;
        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set profile photo visibility to {Visibility} for user {UserId}", visibility, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetPhoneVisibilityAsync(Guid userId, PhoneVisibility visibility)
    {
        var settings = await GetPrivacySettingsAsync(userId);
        settings.PhoneVisibility = visibility;
        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set phone visibility to {Visibility} for user {UserId}", visibility, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetGroupsInCommonVisibilityAsync(Guid userId, bool visible)
    {
        var settings = await GetPrivacySettingsAsync(userId);
        settings.GroupsInCommonVisible = visible;
        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set groups in common visibility to {Visible} for user {UserId}", visible, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetVoiceCallPrivacyAsync(Guid userId, VoiceCallPrivacy privacy)
    {
        var settings = await GetPrivacySettingsAsync(userId);
        settings.VoiceCallPrivacy = privacy;
        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set voice call privacy to {Privacy} for user {UserId}", privacy, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetInvitePrivilegesAsync(Guid userId, InvitePrivileges privileges)
    {
        var settings = await GetPrivacySettingsAsync(userId);
        settings.InvitePrivileges = privileges;
        settings.UpdatedAt = DateTime.UtcNow;

        _context.PrivacySettings.Update(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set invite privileges to {Privileges} for user {UserId}", privileges, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> BlockUserAsync(Guid userId, Guid userToBlock)
    {
        if (userId == userToBlock)
        {
            _logger.LogWarning("User {UserId} attempted to block themselves", userId);
            return false;
        }

        var existingBlock = await _context.BlockedUsers
            .FirstOrDefaultAsync(b => b.UserId == userId && b.BlockedUserId == userToBlock);

        if (existingBlock != null)
        {
            _logger.LogInformation("User {UserToBlock} is already blocked by {UserId}", userToBlock, userId);
            return true;
        }

        var block = new BlockedUser
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BlockedUserId = userToBlock,
            BlockedAt = DateTime.UtcNow
        };

        _context.BlockedUsers.Add(block);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} blocked user {UserToBlock}", userId, userToBlock);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UnblockUserAsync(Guid userId, Guid userToUnblock)
    {
        var block = await _context.BlockedUsers
            .FirstOrDefaultAsync(b => b.UserId == userId && b.BlockedUserId == userToUnblock);

        if (block == null)
        {
            _logger.LogInformation("User {UserToUnblock} is not blocked by {UserId}", userToUnblock, userId);
            return false;
        }

        _context.BlockedUsers.Remove(block);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} unblocked user {UserToUnblock}", userId, userToUnblock);

        return true;
    }

    /// <inheritdoc />
    public async Task<List<Guid>> GetBlockedUsersAsync(Guid userId)
    {
        var blockedUsers = await _context.BlockedUsers
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BlockedAt)
            .Select(b => b.BlockedUserId)
            .ToListAsync();

        return blockedUsers;
    }

    /// <inheritdoc />
    public async Task<bool> AddToPrivacyExceptionsAsync(Guid userId, PrivacyExceptionType type, Guid exceptionUserId)
    {
        var existingException = await _context.PrivacyExceptions
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Type == type && e.ExceptionUserId == exceptionUserId);

        if (existingException != null)
        {
            existingException.IsAllowed = true;
            _context.PrivacyExceptions.Update(existingException);
        }
        else
        {
            var exception = new PrivacyException
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = type,
                ExceptionUserId = exceptionUserId,
                IsAllowed = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.PrivacyExceptions.Add(exception);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Added privacy exception for user {ExceptionUserId} of type {Type} for user {UserId}",
            exceptionUserId, type, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveFromPrivacyExceptionsAsync(Guid userId, PrivacyExceptionType type, Guid exceptionUserId)
    {
        var exception = await _context.PrivacyExceptions
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Type == type && e.ExceptionUserId == exceptionUserId);

        if (exception == null)
        {
            return false;
        }

        _context.PrivacyExceptions.Remove(exception);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Removed privacy exception for user {ExceptionUserId} of type {Type} for user {UserId}",
            exceptionUserId, type, userId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> IsUserBlockedAsync(Guid userId, Guid targetUserId)
    {
        // Check if target blocked the user
        var isBlocked = await _context.BlockedUsers
            .AnyAsync(b => b.UserId == targetUserId && b.BlockedUserId == userId);

        return isBlocked;
    }

    /// <inheritdoc />
    public async Task<bool> CanSendMessageAsync(Guid senderId, Guid recipientId)
    {
        // Check if blocked
        if (await IsUserBlockedAsync(senderId, recipientId))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> CanSeeLastSeenAsync(Guid viewerId, Guid targetUserId)
    {
        // Check if blocked
        if (await IsUserBlockedAsync(viewerId, targetUserId))
        {
            return false;
        }

        var settings = await GetPrivacySettingsAsync(targetUserId);

        return settings.LastSeenVisibility switch
        {
            LastSeenVisibility.Everybody => true,
            LastSeenVisibility.Nobody => false,
            LastSeenVisibility.Contacts => await IsContactAsync(targetUserId, viewerId),
            _ => false
        };
    }

    /// <inheritdoc />
    public async Task<bool> CanSeeProfilePhotoAsync(Guid viewerId, Guid targetUserId)
    {
        // Check if blocked
        if (await IsUserBlockedAsync(viewerId, targetUserId))
        {
            return false;
        }

        var settings = await GetPrivacySettingsAsync(targetUserId);

        return settings.ProfilePhotoVisibility switch
        {
            ProfilePhotoVisibility.Everybody => true,
            ProfilePhotoVisibility.Nobody => false,
            ProfilePhotoVisibility.Contacts => await IsContactAsync(targetUserId, viewerId),
            _ => true
        };
    }

    /// <inheritdoc />
    public async Task<bool> CanCallAsync(Guid callerId, Guid calleeId)
    {
        // Check if blocked
        if (await IsUserBlockedAsync(callerId, calleeId))
        {
            return false;
        }

        var settings = await GetPrivacySettingsAsync(calleeId);

        return settings.VoiceCallPrivacy switch
        {
            VoiceCallPrivacy.Everybody => true,
            VoiceCallPrivacy.Nobody => false,
            VoiceCallPrivacy.Contacts => await IsContactAsync(calleeId, callerId),
            _ => true
        };
    }

    /// <summary>
    /// Check if two users are contacts
    /// </summary>
    private async Task<bool> IsContactAsync(Guid userId, Guid contactId)
    {
        return await _context.Contacts
            .AnyAsync(c => c.UserId == userId && c.ContactUserId == contactId && c.IsActive);
    }
}

/// <summary>
/// Extension methods for PrivacySettingsService
/// </summary>
public static class PrivacySettingsServiceExtensions
{
    /// <summary>
    /// Get privacy summary for a user
    /// </summary>
    public static async Task<PrivacySummary> GetPrivacySummaryAsync(
        this IPrivacySettingsService service,
        Guid userId)
    {
        var settings = await service.GetPrivacySettingsAsync(userId);
        var blockedUsers = await service.GetBlockedUsersAsync(userId);

        return new PrivacySummary
        {
            UserId = userId,
            LastSeenVisibility = settings.LastSeenVisibility.ToString(),
            ProfilePhotoVisibility = settings.ProfilePhotoVisibility.ToString(),
            PhoneVisibility = settings.PhoneVisibility.ToString(),
            GroupsInCommonVisible = settings.GroupsInCommonVisible,
            VoiceCallPrivacy = settings.VoiceCallPrivacy.ToString(),
            InvitePrivileges = settings.InvitePrivileges.ToString(),
            BlockedUsersCount = blockedUsers.Count,
            UpdatedAt = settings.UpdatedAt
        };
    }
}

/// <summary>
/// Privacy settings summary
/// </summary>
public class PrivacySummary
{
    public Guid UserId { get; set; }
    public string LastSeenVisibility { get; set; } = string.Empty;
    public string ProfilePhotoVisibility { get; set; } = string.Empty;
    public string PhoneVisibility { get; set; } = string.Empty;
    public bool GroupsInCommonVisible { get; set; }
    public string VoiceCallPrivacy { get; set; } = string.Empty;
    public string InvitePrivileges { get; set; } = string.Empty;
    public int BlockedUsersCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
