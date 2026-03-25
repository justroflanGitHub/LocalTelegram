using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PushService.Services;
using System.Security.Claims;

namespace PushService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatSettingsController : ControllerBase
{
    private readonly IChatMuteService _muteService;
    private readonly IBadgeCountService _badgeService;
    private readonly ILogger<ChatSettingsController> _logger;

    public ChatSettingsController(
        IChatMuteService muteService,
        IBadgeCountService badgeService,
        ILogger<ChatSettingsController> logger)
    {
        _muteService = muteService;
        _badgeService = badgeService;
        _logger = logger;
    }

    #region Mute/Unmute

    /// <summary>
    /// Mute a chat
    /// </summary>
    [HttpPost("{chatId}/mute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MuteChat(long chatId, [FromQuery] int? durationMinutes)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        TimeSpan? duration = durationMinutes.HasValue 
            ? TimeSpan.FromMinutes(durationMinutes.Value) 
            : null;

        await _muteService.MuteChatAsync(userId.Value, chatId, duration);

        return Ok(new { 
            chatId, 
            muted = true, 
            mutedUntil = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : (DateTime?)null 
        });
    }

    /// <summary>
    /// Unmute a chat
    /// </summary>
    [HttpPost("{chatId}/unmute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnmuteChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _muteService.UnmuteChatAsync(userId.Value, chatId);

        return Ok(new { chatId, muted = false });
    }

    /// <summary>
    /// Get mute status for a chat
    /// </summary>
    [HttpGet("{chatId}/mute")]
    [ProducesResponseType(typeof(ChatMuteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMuteStatus(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var settings = await _muteService.GetMuteSettingsAsync(userId.Value, chatId);

        return Ok(new ChatMuteResponse
        {
            ChatId = chatId,
            IsMuted = settings?.IsMuted ?? false,
            MutedUntil = settings?.MutedUntil,
            IsForever = settings?.IsForever ?? false
        });
    }

    /// <summary>
    /// Get all muted chats
    /// </summary>
    [HttpGet("muted")]
    [ProducesResponseType(typeof(List<ChatMuteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMutedChats()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var mutedChats = await _muteService.GetMutedChatsAsync(userId.Value);

        return Ok(mutedChats.Select(c => new ChatMuteResponse
        {
            ChatId = c.ChatId,
            IsMuted = c.IsMuted,
            MutedUntil = c.MutedUntil,
            IsForever = c.IsForever
        }));
    }

    /// <summary>
    /// Mute all chats
    /// </summary>
    [HttpPost("mute-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MuteAllChats([FromQuery] int? durationMinutes)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        TimeSpan? duration = durationMinutes.HasValue 
            ? TimeSpan.FromMinutes(durationMinutes.Value) 
            : null;

        await _muteService.MuteAllChatsAsync(userId.Value, duration);

        return Ok(new { 
            allMuted = true, 
            mutedUntil = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : (DateTime?)null 
        });
    }

    /// <summary>
    /// Unmute all chats
    /// </summary>
    [HttpPost("unmute-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnmuteAllChats()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _muteService.UnmuteAllChatsAsync(userId.Value);

        return Ok(new { allMuted = false });
    }

    #endregion

    #region Badge Count

    /// <summary>
    /// Get badge count for a specific chat
    /// </summary>
    [HttpGet("{chatId}/badge")]
    [ProducesResponseType(typeof(ChatBadgeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetChatBadge(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var count = await _badgeService.GetChatBadgeCountAsync(userId.Value, chatId);

        return Ok(new ChatBadgeResponse { ChatId = chatId, Count = count });
    }

    /// <summary>
    /// Get total badge count across all chats
    /// </summary>
    [HttpGet("badge-total")]
    [ProducesResponseType(typeof(TotalBadgeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTotalBadge()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var count = await _badgeService.GetTotalBadgeCountAsync(userId.Value);

        return Ok(new TotalBadgeResponse { TotalCount = count });
    }

    /// <summary>
    /// Get badge counts for all chats
    /// </summary>
    [HttpGet("badges")]
    [ProducesResponseType(typeof(AllBadgesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllBadges()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var badges = await _badgeService.GetAllBadgeCountsAsync(userId.Value);

        return Ok(new AllBadgesResponse 
        { 
            Badges = badges.Select(b => new ChatBadgeResponse { ChatId = b.Key, Count = b.Value }).ToList() 
        });
    }

    /// <summary>
    /// Reset badge count for a specific chat
    /// </summary>
    [HttpPost("{chatId}/badge/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetChatBadge(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _badgeService.ResetBadgeCountAsync(userId.Value, chatId);

        return Ok(new { chatId, count = 0 });
    }

    /// <summary>
    /// Reset all badge counts
    /// </summary>
    [HttpPost("badges/reset-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetAllBadges()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _badgeService.ResetAllBadgeCountsAsync(userId.Value);

        return Ok(new { totalCount = 0 });
    }

    #endregion

    private long? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }
}

public class ChatMuteResponse
{
    public long ChatId { get; set; }
    public bool IsMuted { get; set; }
    public DateTime? MutedUntil { get; set; }
    public bool IsForever { get; set; }
}

public class ChatBadgeResponse
{
    public long ChatId { get; set; }
    public int Count { get; set; }
}

public class TotalBadgeResponse
{
    public int TotalCount { get; set; }
}

public class AllBadgesResponse
{
    public List<ChatBadgeResponse> Badges { get; set; } = new();
}
