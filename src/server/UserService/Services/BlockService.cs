using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.Models;

namespace UserService.Services;

public interface IBlockService
{
    Task<BlockedUser?> BlockUserAsync(long userId, long blockedUserId);
    Task<bool> UnblockUserAsync(long userId, long blockedUserId);
    Task<List<BlockedUserDto>> GetBlockedUsersAsync(long userId);
    Task<bool> IsBlockedAsync(long userId, long blockedUserId);
    Task<bool> IsBlockedByAsync(long userId, long byUserId);
}

public class BlockService : IBlockService
{
    private readonly UserDbContext _context;
    private readonly IContactService _contactService;
    private readonly ILogger<BlockService> _logger;

    public BlockService(
        UserDbContext context, 
        IContactService contactService,
        ILogger<BlockService> logger)
    {
        _context = context;
        _contactService = contactService;
        _logger = logger;
    }

    public async Task<BlockedUser?> BlockUserAsync(long userId, long blockedUserId)
    {
        // Check if already blocked
        var existing = await _context.BlockedUsers
            .FirstOrDefaultAsync(b => b.UserId == userId && b.BlockedUserId == blockedUserId);
        
        if (existing != null)
        {
            _logger.LogWarning("User already blocked: {UserId} -> {BlockedUserId}", userId, blockedUserId);
            return existing;
        }

        // Remove from contacts if exists
        await _contactService.RemoveContactAsync(userId, blockedUserId);

        var blockedUser = new BlockedUser
        {
            UserId = userId,
            BlockedUserId = blockedUserId
        };

        _context.BlockedUsers.Add(blockedUser);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Blocked user: {UserId} -> {BlockedUserId}", userId, blockedUserId);
        return blockedUser;
    }

    public async Task<bool> UnblockUserAsync(long userId, long blockedUserId)
    {
        var blockedUser = await _context.BlockedUsers
            .FirstOrDefaultAsync(b => b.UserId == userId && b.BlockedUserId == blockedUserId);
        
        if (blockedUser == null) return false;

        _context.BlockedUsers.Remove(blockedUser);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Unblocked user: {UserId} -> {BlockedUserId}", userId, blockedUserId);
        return true;
    }

    public async Task<List<BlockedUserDto>> GetBlockedUsersAsync(long userId)
    {
        var blockedUsers = await _context.BlockedUsers
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.BlockedAt)
            .ToListAsync();

        var result = new List<BlockedUserDto>();
        foreach (var blocked in blockedUsers)
        {
            var profile = await _context.UserProfiles.FindAsync(blocked.BlockedUserId);
            if (profile != null)
            {
                var avatar = await _context.UserAvatars
                    .FirstOrDefaultAsync(a => a.UserId == blocked.BlockedUserId && a.IsActive);

                result.Add(new BlockedUserDto
                {
                    Id = blocked.Id,
                    BlockedUserId = blocked.BlockedUserId,
                    BlockedAt = blocked.BlockedAt,
                    Profile = UserProfileDto.FromProfile(profile, avatar?.FileId, avatar?.SmallFileId)
                });
            }
        }

        return result;
    }

    public async Task<bool> IsBlockedAsync(long userId, long blockedUserId)
    {
        return await _context.BlockedUsers
            .AnyAsync(b => b.UserId == userId && b.BlockedUserId == blockedUserId);
    }

    public async Task<bool> IsBlockedByAsync(long userId, long byUserId)
    {
        return await _context.BlockedUsers
            .AnyAsync(b => b.UserId == byUserId && b.BlockedUserId == userId);
    }
}

public class BlockedUserDto
{
    public long Id { get; set; }
    public long BlockedUserId { get; set; }
    public DateTime BlockedAt { get; set; }
    public UserProfileDto? Profile { get; set; }
}
