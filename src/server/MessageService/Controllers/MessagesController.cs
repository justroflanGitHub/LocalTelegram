using MessageService.Models;
using MessageService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MessageService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IMessageService messageService, ILogger<MessagesController> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    /// <summary>
    /// Send a new message
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var message = await _messageService.SendMessageAsync(userId.Value, request);
        if (message == null)
        {
            return BadRequest(new { error = "Failed to send message. You may not be a member of this chat." });
        }

        return Ok(MessageDto.FromMessage(message));
    }

    /// <summary>
    /// Get messages for a chat
    /// </summary>
    [HttpGet("chat/{chatId}")]
    [ProducesResponseType(typeof(List<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMessages(
        long chatId,
        [FromQuery] long? beforeId = null,
        [FromQuery] long? afterId = null,
        [FromQuery] int limit = 50)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (limit < 1 || limit > 100)
        {
            limit = 50;
        }

        var messages = await _messageService.GetMessagesAsync(chatId, userId.Value, beforeId, afterId, limit);
        return Ok(messages);
    }

    /// <summary>
    /// Get a single message
    /// </summary>
    [HttpGet("{messageId}")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMessage(long messageId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var message = await _messageService.GetMessageAsync(messageId);
        if (message == null)
        {
            return NotFound();
        }

        return Ok(MessageDto.FromMessage(message));
    }

    /// <summary>
    /// Edit a message
    /// </summary>
    [HttpPut("{messageId}")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditMessage(long messageId, [FromBody] EditMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var message = await _messageService.EditMessageAsync(messageId, userId.Value, request.Content);
        if (message == null)
        {
            return BadRequest(new { error = "Failed to edit message. You may not be the sender." });
        }

        return Ok(MessageDto.FromMessage(message));
    }

    /// <summary>
    /// Delete a message
    /// </summary>
    [HttpDelete("{messageId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMessage(long messageId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _messageService.DeleteMessageAsync(messageId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to delete message" });
        }

        return Ok(new { message = "Message deleted" });
    }

    /// <summary>
    /// Mark a message as read
    /// </summary>
    [HttpPost("{messageId}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAsRead(long messageId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _messageService.MarkAsReadAsync(messageId, userId.Value);
        return Ok(new { message = "Marked as read" });
    }

    /// <summary>
    /// Get unread count for a chat
    /// </summary>
    [HttpGet("chat/{chatId}/unread")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUnreadCount(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var count = await _messageService.GetUnreadCountAsync(chatId, userId.Value);
        return Ok(new { unreadCount = count });
    }

    #region Extended Features

    /// <summary>
    /// Reply to a message
    /// </summary>
    [HttpPost("{messageId}/reply")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReplyToMessage(long messageId, [FromBody] ReplyMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var message = await _messageService.ReplyToMessageAsync(userId.Value, request.ChatId, messageId, request.Content);
        if (message == null)
        {
            return BadRequest(new { error = "Failed to reply to message" });
        }

        return Ok(MessageDto.FromMessage(message));
    }

    /// <summary>
    /// Forward a message to another chat
    /// </summary>
    [HttpPost("{messageId}/forward")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForwardMessage(long messageId, [FromBody] ForwardMessageRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var message = await _messageService.ForwardMessageAsync(userId.Value, messageId, request.TargetChatId);
        if (message == null)
        {
            return BadRequest(new { error = "Failed to forward message" });
        }

        return Ok(MessageDto.FromMessage(message));
    }

    /// <summary>
    /// Forward multiple messages to another chat
    /// </summary>
    [HttpPost("forward-multiple")]
    [ProducesResponseType(typeof(List<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForwardMultipleMessages([FromBody] ForwardMultipleRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var messages = await _messageService.ForwardMultipleMessagesAsync(userId.Value, request.MessageIds, request.TargetChatId);
        return Ok(messages.Select(MessageDto.FromMessage));
    }

    /// <summary>
    /// Search messages in a chat
    /// </summary>
    [HttpGet("chat/{chatId}/search")]
    [ProducesResponseType(typeof(List<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SearchMessages(long chatId, [FromQuery] string query, [FromQuery] int limit = 50)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var messages = await _messageService.SearchMessagesAsync(chatId, userId.Value, query, limit);
        return Ok(messages);
    }

    /// <summary>
    /// Pin a message
    /// </summary>
    [HttpPost("{messageId}/pin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PinMessage(long messageId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _messageService.PinMessageAsync(messageId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to pin message. You may need admin permissions." });
        }

        return Ok(new { message = "Message pinned" });
    }

    /// <summary>
    /// Unpin a message
    /// </summary>
    [HttpPost("{messageId}/unpin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UnpinMessage(long messageId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _messageService.UnpinMessageAsync(messageId, userId.Value);
        if (!success)
        {
            return BadRequest(new { error = "Failed to unpin message" });
        }

        return Ok(new { message = "Message unpinned" });
    }

    /// <summary>
    /// Get pinned messages in a chat
    /// </summary>
    [HttpGet("chat/{chatId}/pinned")]
    [ProducesResponseType(typeof(List<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPinnedMessages(long chatId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var messages = await _messageService.GetPinnedMessagesAsync(chatId, userId.Value);
        return Ok(messages);
    }

    /// <summary>
    /// Add a reaction to a message
    /// </summary>
    [HttpPost("{messageId}/reactions")]
    [ProducesResponseType(typeof(MessageReactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddReaction(long messageId, [FromBody] AddReactionRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var reaction = await _messageService.AddReactionAsync(messageId, userId.Value, request.Emoji);
        if (reaction == null)
        {
            return BadRequest(new { error = "Failed to add reaction" });
        }

        return Ok(new { emoji = reaction.Emoji, createdAt = reaction.CreatedAt });
    }

    /// <summary>
    /// Remove a reaction from a message
    /// </summary>
    [HttpDelete("{messageId}/reactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveReaction(long messageId, [FromQuery] string emoji)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var success = await _messageService.RemoveReactionAsync(messageId, userId.Value, emoji);
        if (!success)
        {
            return BadRequest(new { error = "Failed to remove reaction" });
        }

        return Ok(new { message = "Reaction removed" });
    }

    /// <summary>
    /// Get reactions for a message
    /// </summary>
    [HttpGet("{messageId}/reactions")]
    [ProducesResponseType(typeof(List<MessageReactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetReactions(long messageId)
    {
        var reactions = await _messageService.GetReactionsAsync(messageId);
        return Ok(reactions);
    }

    #endregion

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
