using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.Models;

namespace UserService.Services;

public interface IUserProfileService
{
    Task<UserProfile?> GetProfileAsync(long userId);
    Task<UserProfileDto?> GetProfileDtoAsync(long userId, long? viewerId = null);
    Task<UserProfile?> UpdateProfileAsync(long userId, UpdateProfileRequest request);
    Task<UserAvatar?> SetAvatarAsync(long userId, string fileId, string smallFileId);
    Task<UserAvatar?> GetActiveAvatarAsync(long userId);
    Task<bool> DeleteAvatarAsync(long userId);
    Task<List<UserProfileDto>> SearchUsersAsync(string query, int limit = 20);
    Task<bool> UserExistsAsync(long userId);
    Task CreateProfileAsync(UserProfile profile);
}

public class UserProfileService : IUserProfileService
{
    private readonly UserDbContext _context;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(UserDbContext context, ILogger<UserProfileService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserProfile?> GetProfileAsync(long userId)
    {
        return await _context.UserProfiles.FindAsync(userId);
    }

    public async Task<UserProfileDto?> GetProfileDtoAsync(long userId, long? viewerId = null)
    {
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null) return null;

        var avatar = await GetActiveAvatarAsync(userId);
        bool isContact = false;
        bool isBlocked = false;

        if (viewerId.HasValue)
        {
            isContact = await _context.Contacts
                .AnyAsync(c => c.UserId == viewerId.Value && c.ContactUserId == userId);
            
            isBlocked = await _context.BlockedUsers
                .AnyAsync(b => b.UserId == viewerId.Value && b.BlockedUserId == userId);
        }

        return UserProfileDto.FromProfile(profile, avatar?.FileId, avatar?.SmallFileId, isContact, isBlocked);
    }

    public async Task<UserProfile?> UpdateProfileAsync(long userId, UpdateProfileRequest request)
    {
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null) return null;

        if (request.FirstName != null) profile.FirstName = request.FirstName;
        if (request.LastName != null) profile.LastName = request.LastName;
        if (request.Bio != null) profile.Bio = request.Bio;

        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated profile for user {UserId}", userId);
        return profile;
    }

    public async Task<UserAvatar?> SetAvatarAsync(long userId, string fileId, string smallFileId)
    {
        // Deactivate current avatar
        var currentAvatars = await _context.UserAvatars
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync();
        
        foreach (var avatar in currentAvatars)
        {
            avatar.IsActive = false;
        }

        // Create new avatar
        var newAvatar = new UserAvatar
        {
            UserId = userId,
            FileId = fileId,
            SmallFileId = smallFileId,
            IsActive = true
        };

        _context.UserAvatars.Add(newAvatar);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Set new avatar for user {UserId}", userId);
        return newAvatar;
    }

    public async Task<UserAvatar?> GetActiveAvatarAsync(long userId)
    {
        return await _context.UserAvatars
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderByDescending(a => a.UploadedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> DeleteAvatarAsync(long userId)
    {
        var avatars = await _context.UserAvatars
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync();

        if (!avatars.Any()) return false;

        foreach (var avatar in avatars)
        {
            avatar.IsActive = false;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted avatar for user {UserId}", userId);
        return true;
    }

    public async Task<List<UserProfileDto>> SearchUsersAsync(string query, int limit = 20)
    {
        var normalizedQuery = query.ToLowerInvariant();

        var profiles = await _context.UserProfiles
            .Where(p => p.IsActive && 
                   (p.Username.ToLower().Contains(normalizedQuery) ||
                    (p.FirstName != null && p.FirstName.ToLower().Contains(normalizedQuery)) ||
                    (p.LastName != null && p.LastName.ToLower().Contains(normalizedQuery))))
            .Take(limit)
            .ToListAsync();

        var result = new List<UserProfileDto>();
        foreach (var profile in profiles)
        {
            var avatar = await GetActiveAvatarAsync(profile.UserId);
            result.Add(UserProfileDto.FromProfile(profile, avatar?.FileId, avatar?.SmallFileId));
        }

        return result;
    }

    public async Task<bool> UserExistsAsync(long userId)
    {
        return await _context.UserProfiles.AnyAsync(p => p.UserId == userId);
    }

    public async Task CreateProfileAsync(UserProfile profile)
    {
        _context.UserProfiles.Add(profile);
        await _context.SaveChangesAsync();

        // Create default privacy settings
        var privacySettings = new PrivacySetting { UserId = profile.UserId };
        _context.PrivacySettings.Add(privacySettings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created profile for user {UserId}", profile.UserId);
    }
}
