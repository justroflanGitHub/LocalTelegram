using MessageService.Models;
using MessageService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MessageService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatsController> _logger;

    public ChatsController(IChatService chatService, ILogger<ChatsController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Get all chats for current user
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ChatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetChats()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chats = await _chatService.GetUserChatsAsync(userId.Value);
        return Ok(chats);
    }

    /// <summary>
    /// Create a new chat
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateChat([FromBody] CreateChatRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (request.Type == ChatType.Private && request.MemberIds.Count != 1)
        {
            return BadRequest(new { error = "Private chat must have exactly one other member" });
        }

        if (request.Type != ChatType.Private && string.IsNullOrEmpty(request.Title))
        {
            return BadRequest(new { error = "Group chats must have a title" });
        }

        var chat = await _chatService.CreateChatAsync(userId.Value, request);
        if (chat == null)
        {
            return BadRequest(new { error = "Failed to create chat" });
        }

        return Ok(ChatDto.FromChat(chat));
    }

    /// <summary>
    /// Get a specific chat
    /// </summary>
    [HttpGet("{chatId}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chat = await _chatService.GetChatAsync(chatId, userId.Value);
        if (chat == null)
        {
            return NotFound();
        }

        return Ok(ChatDto.FromChat(chat));
    }

    /// <summary>
    /// Update a chat
    /// </summary>
    [HttpPut("{chatId}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateChat(long chatId, [FromBody] UpdateChatRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chat = await _chatService.UpdateChatAsync(chatId, userId.Value, request);
        if (chat == null)
        {
            return BadRequest(new { error = "Failed to update chat. You may not have permission." });
        }

        return Ok(ChatDto.FromChat(chat));
    }

    /// <summary>
    /// Delete a chat
    /// </summary>
    [HttpDelete("{chatId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteChat(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _chatService.DeleteChatAsync(chatId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to delete chat. Only the owner can delete a chat." });
        }

        return Ok(new { message = "Chat deleted" });
    }

    /// <summary>
    /// Get chat members
    /// </summary>
    [HttpGet("{chatId}/members")]
    [ProducesResponseType(typeof(List<ChatMember>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetChatMembers(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var members = await _chatService.GetChatMembersAsync(chatId, userId.Value);
        return Ok(members);
    }

    /// <summary>
    /// Add member to chat
    /// </summary>
    [HttpPost("{chatId}/members")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddMember(long chatId, [FromBody] AddMemberRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _chatService.AddMemberAsync(chatId, request.UserId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to add member. You may not have permission or the chat is private." });
        }

        return Ok(new { message = "Member added" });
    }

    /// <summary>
    /// Remove member from chat
    /// </summary>
    [HttpDelete("{chatId}/members/{memberUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveMember(long chatId, long memberUserId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _chatService.RemoveMemberAsync(chatId, memberUserId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to remove member. You may not have permission." });
        }

        return Ok(new { message = "Member removed" });
    }

    /// <summary>
    /// Update member role
    /// </summary>
    [HttpPut("{chatId}/members/{memberUserId}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMemberRole(long chatId, long memberUserId, [FromBody] UpdateRoleRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _chatService.UpdateMemberRoleAsync(chatId, memberUserId, request.Role, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to update role. Only the owner can change roles." });
        }

        return Ok(new { message = "Role updated" });
    }

    /// <summary>
    /// Get or create private chat with a user
    /// </summary>
    [HttpPost("private/{otherUserId}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrCreatePrivateChat(long otherUserId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var chat = await _chatService.GetOrCreatePrivateChatAsync(userId.Value, otherUserId);
        if (chat == null)
        {
            return BadRequest(new { error = "Failed to create private chat" });
        }

        return Ok(ChatDto.FromChat(chat));
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

public class UpdateRoleRequest
{
    public MemberRole Role { get; set; }
}
