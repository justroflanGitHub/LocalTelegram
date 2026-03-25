using FileService.Models;
using FileService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FileService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VoiceController : ControllerBase
{
    private readonly IVoiceMessageService _voiceService;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        IVoiceMessageService voiceService,
        ILogger<VoiceController> logger)
    {
        _voiceService = voiceService;
        _logger = logger;
    }

    /// <summary>
    /// Upload a voice message
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(100_000_000)] // 100MB
    [ProducesResponseType(typeof(VoiceMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadVoiceMessage(
        [FromForm] IFormFile audioFile,
        [FromForm] int durationSeconds,
        [FromForm] string? waveform)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest(new { error = "No audio file provided" });
        }

        if (durationSeconds <= 0)
        {
            return BadRequest(new { error = "Invalid duration" });
        }

        var voiceMessage = await _voiceService.UploadVoiceMessageAsync(
            userId.Value, 
            audioFile, 
            durationSeconds,
            waveform);

        if (voiceMessage == null)
        {
            return BadRequest(new { error = "Failed to upload voice message" });
        }

        var downloadUrl = await _voiceService.GetVoiceMessageUrlAsync(voiceMessage.Id);

        return Ok(VoiceMessageDto.FromVoiceMessage(voiceMessage, downloadUrl));
    }

    /// <summary>
    /// Get voice message metadata
    /// </summary>
    [HttpGet("{voiceId}")]
    [ProducesResponseType(typeof(VoiceMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetVoiceMessage(long voiceId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var voiceMessage = await _voiceService.GetVoiceMessageAsync(voiceId);
        if (voiceMessage == null)
        {
            return NotFound();
        }

        var downloadUrl = await _voiceService.GetVoiceMessageUrlAsync(voiceId);

        return Ok(VoiceMessageDto.FromVoiceMessage(voiceMessage, downloadUrl));
    }

    /// <summary>
    /// Download a voice message
    /// </summary>
    [HttpGet("{voiceId}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadVoiceMessage(long voiceId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var voiceMessage = await _voiceService.GetVoiceMessageAsync(voiceId);
        if (voiceMessage == null)
        {
            return NotFound();
        }

        var stream = await _voiceService.DownloadVoiceMessageAsync(voiceId);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, "audio/ogg", $"voice_{voiceId}.ogg");
    }

    /// <summary>
    /// Get presigned URL for voice message download
    /// </summary>
    [HttpGet("{voiceId}/url")]
    [ProducesResponseType(typeof(DownloadUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetVoiceMessageUrl(long voiceId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var url = await _voiceService.GetVoiceMessageUrlAsync(voiceId);
        if (url == null)
        {
            return NotFound();
        }

        return Ok(new DownloadUrlResponse { Url = url });
    }

    /// <summary>
    /// Delete a voice message
    /// </summary>
    [HttpDelete("{voiceId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteVoiceMessage(long voiceId)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _voiceService.DeleteVoiceMessageAsync(voiceId, userId.Value);
        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

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
