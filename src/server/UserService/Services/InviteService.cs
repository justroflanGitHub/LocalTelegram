using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserService.Data;

namespace UserService.Services
{
    /// <summary>
    /// Invite system for user registration via email or link
    /// </summary>
    public class InviteService
    {
        private readonly UserDbContext _context;
        private readonly ILogger<InviteService> _logger;
        private readonly IEmailService _emailService;

        public InviteService(UserDbContext context, ILogger<InviteService> logger, IEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
        }

        /// <summary>
        /// Creates an email invitation
        /// </summary>
        public async Task<Invite> CreateEmailInviteAsync(Guid createdBy, string email, 
            Guid? groupId = null, string? welcomeMessage = null, TimeSpan? expiration = null)
        {
            var invite = new Invite
            {
                Id = Guid.NewGuid(),
                Code = GenerateInviteCode(),
                Type = InviteType.Email,
                Email = email.ToLowerInvariant(),
                CreatedBy = createdBy,
                GroupId = groupId,
                WelcomeMessage = welcomeMessage,
                ExpiresAt = expiration.HasValue 
                    ? DateTime.UtcNow.Add(expiration.Value) 
                    : DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                IsUsed = false
            };

            _context.Invites.Add(invite);
            await _context.SaveChangesAsync();

            // Send invitation email
            await _emailService.SendInviteEmailAsync(email, invite.Code, welcomeMessage);

            _logger.LogInformation("Created email invite for {Email} by user {UserId}", email, createdBy);

            return invite;
        }

        /// <summary>
        /// Creates a shareable invite link
        /// </summary>
        public async Task<Invite> CreateLinkInviteAsync(Guid createdBy, 
            Guid? groupId = null, int? maxUses = null, TimeSpan? expiration = null)
        {
            var invite = new Invite
            {
                Id = Guid.NewGuid(),
                Code = GenerateInviteCode(),
                Type = InviteType.Link,
                CreatedBy = createdBy,
                GroupId = groupId,
                MaxUses = maxUses,
                ExpiresAt = expiration.HasValue 
                    ? DateTime.UtcNow.Add(expiration.Value) 
                    : DateTime.UtcNow.AddDays(30),
                CreatedAt = DateTime.UtcNow,
                IsUsed = false
            };

            _context.Invites.Add(invite);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created link invite {Code} by user {UserId}", invite.Code, createdBy);

            return invite;
        }

        /// <summary>
        /// Validates an invite code
        /// </summary>
        public async Task<InviteValidationResult> ValidateInviteAsync(string code)
        {
            var invite = await _context.Invites
                .FirstOrDefaultAsync(i => i.Code == code);

            if (invite == null)
            {
                return new InviteValidationResult
                {
                    IsValid = false,
                    Error = "Invalid invite code"
                };
            }

            if (invite.IsUsed && invite.Type == InviteType.Email)
            {
                return new InviteValidationResult
                {
                    IsValid = false,
                    Error = "This invite has already been used"
                };
            }

            if (invite.ExpiresAt < DateTime.UtcNow)
            {
                return new InviteValidationResult
                {
                    IsValid = false,
                    Error = "This invite has expired"
                };
            }

            if (invite.MaxUses.HasValue && invite.UseCount >= invite.MaxUses.Value)
            {
                return new InviteValidationResult
                {
                    IsValid = false,
                    Error = "This invite has reached its maximum uses"
                };
            }

            return new InviteValidationResult
            {
                IsValid = true,
                Invite = invite
            };
        }

        /// <summary>
        /// Uses an invite code (marks as used or increments use count)
        /// </summary>
        public async Task<bool> UseInviteAsync(string code, Guid usedBy)
        {
            var invite = await _context.Invites.FirstOrDefaultAsync(i => i.Code == code);
            if (invite == null)
            {
                return false;
            }

            invite.UseCount++;
            invite.UsedBy = usedBy;
            invite.UsedAt = DateTime.UtcNow;

            if (invite.Type == InviteType.Email)
            {
                invite.IsUsed = true;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Invite {Code} used by user {UserId}", code, usedBy);

            return true;
        }

        /// <summary>
        /// Gets all invites created by a user
        /// </summary>
        public async Task<List<Invite>> GetUserInvitesAsync(Guid userId)
        {
            return await _context.Invites
                .Where(i => i.CreatedBy == userId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Gets all active invites for a group
        /// </summary>
        public async Task<List<Invite>> GetGroupInvitesAsync(Guid groupId)
        {
            return await _context.Invites
                .Where(i => i.GroupId == groupId && !i.IsUsed && i.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Revokes an invite
        /// </summary>
        public async Task<bool> RevokeInviteAsync(Guid inviteId, Guid userId)
        {
            var invite = await _context.Invites
                .FirstOrDefaultAsync(i => i.Id == inviteId && i.CreatedBy == userId);

            if (invite == null)
            {
                return false;
            }

            invite.IsRevoked = true;
            invite.RevokedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Invite {InviteId} revoked by user {UserId}", inviteId, userId);

            return true;
        }

        /// <summary>
        /// Cleans up expired invites
        /// </summary>
        public async Task<int> CleanupExpiredInvitesAsync()
        {
            var expiredInvites = await _context.Invites
                .Where(i => i.ExpiresAt < DateTime.UtcNow.AddDays(-30))
                .ToListAsync();

            _context.Invites.RemoveRange(expiredInvites);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} expired invites", expiredInvites.Count);

            return expiredInvites.Count;
        }

        /// <summary>
        /// Gets invite statistics
        /// </summary>
        public async Task<InviteStats> GetInviteStatsAsync(Guid? userId = null)
        {
            var query = _context.Invites.AsQueryable();

            if (userId.HasValue)
            {
                query = query.Where(i => i.CreatedBy == userId.Value);
            }

            var invites = await query.ToListAsync();

            return new InviteStats
            {
                TotalInvites = invites.Count,
                ActiveInvites = invites.Count(i => !i.IsUsed && !i.IsRevoked && i.ExpiresAt > DateTime.UtcNow),
                UsedInvites = invites.Count(i => i.IsUsed || i.UseCount > 0),
                ExpiredInvites = invites.Count(i => i.ExpiresAt < DateTime.UtcNow),
                RevokedInvites = invites.Count(i => i.IsRevoked),
                EmailInvites = invites.Count(i => i.Type == InviteType.Email),
                LinkInvites = invites.Count(i => i.Type == InviteType.Link)
            };
        }

        private string GenerateInviteCode()
        {
            // Generate a random 12-character code
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Removed confusing characters
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[12];
            rng.GetBytes(bytes);

            var code = new char[12];
            for (var i = 0; i < 12; i++)
            {
                code[i] = chars[bytes[i] % chars.Length];
            }

            return new string(code);
        }
    }

    #region Models

    public class Invite
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public InviteType Type { get; set; }
        public string? Email { get; set; }
        public Guid CreatedBy { get; set; }
        public Guid? GroupId { get; set; }
        public string? WelcomeMessage { get; set; }
        public int? MaxUses { get; set; }
        public int UseCount { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsUsed { get; set; }
        public Guid? UsedBy { get; set; }
        public DateTime? UsedAt { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    public enum InviteType
    {
        Email,
        Link
    }

    public class InviteValidationResult
    {
        public bool IsValid { get; set; }
        public string? Error { get; set; }
        public Invite? Invite { get; set; }
    }

    public class InviteStats
    {
        public int TotalInvites { get; set; }
        public int ActiveInvites { get; set; }
        public int UsedInvites { get; set; }
        public int ExpiredInvites { get; set; }
        public int RevokedInvites { get; set; }
        public int EmailInvites { get; set; }
        public int LinkInvites { get; set; }
    }

    #endregion

    #region Email Service Interface

    public interface IEmailService
    {
        Task SendInviteEmailAsync(string email, string code, string? welcomeMessage);
    }

    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;
        }

        public Task SendInviteEmailAsync(string email, string code, string? welcomeMessage)
        {
            // TODO: Implement actual email sending
            _logger.LogInformation("Sending invite email to {Email} with code {Code}", email, code);
            return Task.CompletedTask;
        }
    }

    #endregion
}
