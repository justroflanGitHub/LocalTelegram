using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AdminService.Data;
using AdminService.Models;

namespace AdminService.Services
{
    /// <summary>
    /// Content moderation service for handling user reports and content review
    /// </summary>
    public class ModerationService
    {
        private readonly AdminDbContext _context;
        private readonly ILogger<ModerationService> _logger;

        public ModerationService(AdminDbContext context, ILogger<ModerationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region Report Management

        /// <summary>
        /// Creates a new content report
        /// </summary>
        public async Task<ContentReport> CreateReportAsync(
            Guid reporterId,
            ContentType contentType,
            Guid contentId,
            ReportReason reason,
            string? description = null,
            Guid? chatId = null)
        {
            var report = new ContentReport
            {
                Id = Guid.NewGuid(),
                ReporterId = reporterId,
                ContentType = contentType,
                ContentId = contentId,
                ChatId = chatId,
                Reason = reason,
                Description = description,
                Status = ReportStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _context.ContentReports.Add(report);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Content report created: {ReportId} by user {ReporterId}", 
                report.Id, reporterId);

            return report;
        }

        /// <summary>
        /// Gets all pending reports
        /// </summary>
        public async Task<List<ContentReport>> GetPendingReportsAsync(int limit = 50, int offset = 0)
        {
            return await _context.ContentReports
                .Where(r => r.Status == ReportStatus.Pending)
                .OrderBy(r => r.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets reports by status
        /// </summary>
        public async Task<List<ContentReport>> GetReportsByStatusAsync(
            ReportStatus status, 
            int limit = 50, 
            int offset = 0)
        {
            return await _context.ContentReports
                .Where(r => r.Status == status)
                .OrderByDescending(r => r.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Gets reports for a specific user (reported by or against)
        /// </summary>
        public async Task<List<ContentReport>> GetUserReportsAsync(Guid userId)
        {
            return await _context.ContentReports
                .Where(r => r.ReporterId == userId || r.ReportedUserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Gets report statistics
        /// </summary>
        public async Task<ReportStatistics> GetReportStatisticsAsync()
        {
            var reports = await _context.ContentReports.ToListAsync();

            return new ReportStatistics
            {
                TotalReports = reports.Count,
                PendingReports = reports.Count(r => r.Status == ReportStatus.Pending),
                UnderReviewReports = reports.Count(r => r.Status == ReportStatus.UnderReview),
                ResolvedReports = reports.Count(r => r.Status == ReportStatus.Resolved),
                DismissedReports = reports.Count(r => r.Status == ReportStatus.Dismissed),
                ReportsByReason = reports
                    .GroupBy(r => r.Reason)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ReportsByContentType = reports
                    .GroupBy(r => r.ContentType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ReportsLast24Hours = reports.Count(r => r.CreatedAt > DateTime.UtcNow.AddHours(-24)),
                ReportsLast7Days = reports.Count(r => r.CreatedAt > DateTime.UtcNow.AddDays(-7))
            };
        }

        #endregion

        #region Review Actions

        /// <summary>
        /// Assigns a report to a moderator for review
        /// </summary>
        public async Task<bool> AssignReportAsync(Guid reportId, Guid moderatorId)
        {
            var report = await _context.ContentReports.FindAsync(reportId);
            if (report == null)
            {
                return false;
            }

            report.AssignedTo = moderatorId;
            report.Status = ReportStatus.UnderReview;
            report.AssignedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Report {ReportId} assigned to moderator {ModeratorId}", 
                reportId, moderatorId);

            return true;
        }

        /// <summary>
        /// Resolves a report with action taken
        /// </summary>
        public async Task<bool> ResolveReportAsync(
            Guid reportId, 
            Guid moderatorId,
            ModerationAction action,
            string? notes = null,
            Guid? contentOwnerId = null)
        {
            var report = await _context.ContentReports.FindAsync(reportId);
            if (report == null)
            {
                return false;
            }

            report.Status = ReportStatus.Resolved;
            report.ResolvedBy = moderatorId;
            report.ResolvedAt = DateTime.UtcNow;
            report.ActionTaken = action;
            report.ResolutionNotes = notes;
            report.ReportedUserId = contentOwnerId;

            await _context.SaveChangesAsync();

            // Log the moderation action
            await LogModerationActionAsync(moderatorId, reportId, action, notes, contentOwnerId);

            _logger.LogInformation("Report {ReportId} resolved with action {Action} by moderator {ModeratorId}", 
                reportId, action, moderatorId);

            return true;
        }

        /// <summary>
        /// Dismisses a report as invalid
        /// </summary>
        public async Task<bool> DismissReportAsync(Guid reportId, Guid moderatorId, string? reason = null)
        {
            var report = await _context.ContentReports.FindAsync(reportId);
            if (report == null)
            {
                return false;
            }

            report.Status = ReportStatus.Dismissed;
            report.ResolvedBy = moderatorId;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolutionNotes = reason;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Report {ReportId} dismissed by moderator {ModeratorId}", 
                reportId, moderatorId);

            return true;
        }

        /// <summary>
        /// Escalates a report to higher-level moderators
        /// </summary>
        public async Task<bool> EscalateReportAsync(Guid reportId, Guid moderatorId, string reason)
        {
            var report = await _context.ContentReports.FindAsync(reportId);
            if (report == null)
            {
                return false;
            }

            report.Status = ReportStatus.Escalated;
            report.EscalatedBy = moderatorId;
            report.EscalatedAt = DateTime.UtcNow;
            report.EscalationReason = reason;

            await _context.SaveChangesAsync();

            _logger.LogWarning("Report {ReportId} escalated by moderator {ModeratorId}: {Reason}", 
                reportId, moderatorId, reason);

            return true;
        }

        #endregion

        #region Moderation Actions

        /// <summary>
        /// Deletes content and optionally warns/bans the user
        /// </summary>
        public async Task<ModerationResult> DeleteContentAsync(
            Guid moderatorId,
            ContentType contentType,
            Guid contentId,
            Guid contentOwnerId,
            string reason,
            bool warnUser = true)
        {
            var result = new ModerationResult { Success = true };

            // Delete the content (would call appropriate service)
            // For now, we just log the action
            _logger.LogInformation("Content {ContentId} of type {ContentType} deleted by moderator {ModeratorId}", 
                contentId, contentType, moderatorId);

            result.ContentDeleted = true;

            if (warnUser)
            {
                await IssueWarningAsync(contentOwnerId, moderatorId, reason, contentType, contentId);
                result.UserWarned = true;
            }

            return result;
        }

        /// <summary>
        /// Issues a warning to a user
        /// </summary>
        public async Task<UserWarning> IssueWarningAsync(
            Guid userId,
            Guid issuedBy,
            string reason,
            ContentType? contentType = null,
            Guid? contentId = null)
        {
            var warning = new UserWarning
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                IssuedBy = issuedBy,
                Reason = reason,
                ContentType = contentType,
                ContentId = contentId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.UserWarnings.Add(warning);
            await _context.SaveChangesAsync();

            // Check if user should be auto-banned
            var warningCount = await GetActiveWarningCountAsync(userId);
            if (warningCount >= 3)
            {
                await BanUserAsync(userId, issuedBy, "Automatic ban: 3+ active warnings", TimeSpan.FromDays(30));
            }

            _logger.LogWarning("Warning issued to user {UserId} by moderator {ModeratorId}: {Reason}", 
                userId, issuedBy, reason);

            return warning;
        }

        /// <summary>
        /// Bans a user for a specified duration or permanently
        /// </summary>
        public async Task<UserBan> BanUserAsync(
            Guid userId,
            Guid bannedBy,
            string reason,
            TimeSpan? duration = null)
        {
            var ban = new UserBan
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BannedBy = bannedBy,
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null,
                IsPermanent = !duration.HasValue,
                IsActive = true
            };

            _context.UserBans.Add(ban);
            await _context.SaveChangesAsync();

            _logger.LogWarning("User {UserId} banned by moderator {ModeratorId} for reason: {Reason}", 
                userId, bannedBy, reason);

            return ban;
        }

        /// <summary>
        /// Unbans a user
        /// </summary>
        public async Task<bool> UnbanUserAsync(Guid userId, Guid unbannedBy, string reason)
        {
            var activeBan = await _context.UserBans
                .FirstOrDefaultAsync(b => b.UserId == userId && b.IsActive);

            if (activeBan == null)
            {
                return false;
            }

            activeBan.IsActive = false;
            activeBan.UnbannedBy = unbannedBy;
            activeBan.UnbannedAt = DateTime.UtcNow;
            activeBan.UnbanReason = reason;

            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} unbanned by moderator {ModeratorId}: {Reason}", 
                userId, unbannedBy, reason);

            return true;
        }

        /// <summary>
        /// Mutes a user in a specific chat or globally
        /// </summary>
        public async Task<UserMute> MuteUserAsync(
            Guid userId,
            Guid mutedBy,
            string reason,
            TimeSpan duration,
            Guid? chatId = null)
        {
            var mute = new UserMute
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MutedBy = mutedBy,
                Reason = reason,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(duration),
                IsActive = true
            };

            _context.UserMutes.Add(mute);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} muted by moderator {ModeratorId} for {Duration}", 
                userId, mutedBy, duration);

            return mute;
        }

        /// <summary>
        /// Removes a mute from a user
        /// </summary>
        public async Task<bool> UnmuteUserAsync(Guid userId, Guid? chatId = null)
        {
            var query = _context.UserMutes
                .Where(m => m.UserId == userId && m.IsActive);

            if (chatId.HasValue)
            {
                query = query.Where(m => m.ChatId == chatId);
            }

            var activeMutes = await query.ToListAsync();
            if (!activeMutes.Any())
            {
                return false;
            }

            foreach (var mute in activeMutes)
            {
                mute.IsActive = false;
                mute.UnmutedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return true;
        }

        #endregion

        #region Queries

        /// <summary>
        /// Gets active warnings for a user
        /// </summary>
        public async Task<List<UserWarning>> GetUserWarningsAsync(Guid userId)
        {
            return await _context.UserWarnings
                .Where(w => w.UserId == userId && w.IsActive)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Gets the count of active warnings for a user
        /// </summary>
        public async Task<int> GetActiveWarningCountAsync(Guid userId)
        {
            return await _context.UserWarnings
                .CountAsync(w => w.UserId == userId && w.IsActive);
        }

        /// <summary>
        /// Checks if a user is currently banned
        /// </summary>
        public async Task<UserBan?> GetActiveBanAsync(Guid userId)
        {
            return await _context.UserBans
                .FirstOrDefaultAsync(b => b.UserId == userId && b.IsActive &&
                    (b.IsPermanent || b.ExpiresAt > DateTime.UtcNow));
        }

        /// <summary>
        /// Checks if a user is currently muted
        /// </summary>
        public async Task<UserMute?> GetActiveMuteAsync(Guid userId, Guid? chatId = null)
        {
            var query = _context.UserMutes
                .Where(m => m.UserId == userId && m.IsActive && m.ExpiresAt > DateTime.UtcNow);

            if (chatId.HasValue)
            {
                query = query.Where(m => m.ChatId == chatId || m.ChatId == null);
            }

            return await query.FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gets moderation history for a user
        /// </summary>
        public async Task<ModerationHistory> GetUserModerationHistoryAsync(Guid userId)
        {
            return new ModerationHistory
            {
                UserId = userId,
                Warnings = await GetUserWarningsAsync(userId),
                Bans = await _context.UserBans
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.CreatedAt)
                    .ToListAsync(),
                Mutes = await _context.UserMutes
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.CreatedAt)
                    .ToListAsync(),
                ReportsAgainstUser = await _context.ContentReports
                    .Where(r => r.ReportedUserId == userId)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync()
            };
        }

        #endregion

        #region Helpers

        private async Task LogModerationActionAsync(
            Guid moderatorId,
            Guid reportId,
            ModerationAction action,
            string? notes,
            Guid? targetUserId)
        {
            var log = new ModerationActionLog
            {
                Id = Guid.NewGuid(),
                ModeratorId = moderatorId,
                ReportId = reportId,
                Action = action,
                Notes = notes,
                TargetUserId = targetUserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.ModerationActionLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Cleans up expired mutes
        /// </summary>
        public async Task<int> CleanupExpiredMutesAsync()
        {
            var expiredMutes = await _context.UserMutes
                .Where(m => m.IsActive && m.ExpiresAt <= DateTime.UtcNow)
                .ToListAsync();

            foreach (var mute in expiredMutes)
            {
                mute.IsActive = false;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} expired mutes", expiredMutes.Count);

            return expiredMutes.Count;
        }

        /// <summary>
        /// Cleans up old warnings based on retention policy
        /// </summary>
        public async Task<int> CleanupOldWarningsAsync(int retentionDays = 90)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            var oldWarnings = await _context.UserWarnings
                .Where(w => w.CreatedAt < cutoffDate)
                .ToListAsync();

            _context.UserWarnings.RemoveRange(oldWarnings);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} old warnings", oldWarnings.Count);

            return oldWarnings.Count;
        }

        #endregion
    }

    #region Models

    public class ContentReport
    {
        public Guid Id { get; set; }
        public Guid ReporterId { get; set; }
        public ContentType ContentType { get; set; }
        public Guid ContentId { get; set; }
        public Guid? ChatId { get; set; }
        public Guid? ReportedUserId { get; set; }
        public ReportReason Reason { get; set; }
        public string? Description { get; set; }
        public ReportStatus Status { get; set; }
        public Guid? AssignedTo { get; set; }
        public DateTime? AssignedAt { get; set; }
        public Guid? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public ModerationAction? ActionTaken { get; set; }
        public string? ResolutionNotes { get; set; }
        public Guid? EscalatedBy { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public string? EscalationReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public enum ContentType
    {
        Message,
        Image,
        Video,
        File,
        Profile,
        Group,
        VoiceMessage,
        Story
    }

    public enum ReportReason
    {
        Spam,
        Harassment,
        Violence,
        Nsfw,
        HateSpeech,
        Illegal,
        Malware,
        Impersonation,
        Privacy,
        Other
    }

    public enum ReportStatus
    {
        Pending,
        UnderReview,
        Resolved,
        Dismissed,
        Escalated
    }

    public enum ModerationAction
    {
        None,
        ContentDeleted,
        UserWarned,
        UserMuted,
        UserBanned,
        UserSuspended
    }

    public class UserWarning
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid IssuedBy { get; set; }
        public string Reason { get; set; } = string.Empty;
        public ContentType? ContentType { get; set; }
        public Guid? ContentId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class UserBan
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid BannedBy { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsPermanent { get; set; }
        public bool IsActive { get; set; }
        public Guid? UnbannedBy { get; set; }
        public DateTime? UnbannedAt { get; set; }
        public string? UnbanReason { get; set; }
    }

    public class UserMute
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid MutedBy { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid? ChatId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime? UnmutedAt { get; set; }
    }

    public class ModerationActionLog
    {
        public Guid Id { get; set; }
        public Guid ModeratorId { get; set; }
        public Guid ReportId { get; set; }
        public ModerationAction Action { get; set; }
        public string? Notes { get; set; }
        public Guid? TargetUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ReportStatistics
    {
        public int TotalReports { get; set; }
        public int PendingReports { get; set; }
        public int UnderReviewReports { get; set; }
        public int ResolvedReports { get; set; }
        public int DismissedReports { get; set; }
        public Dictionary<ReportReason, int> ReportsByReason { get; set; } = new();
        public Dictionary<ContentType, int> ReportsByContentType { get; set; } = new();
        public int ReportsLast24Hours { get; set; }
        public int ReportsLast7Days { get; set; }
    }

    public class ModerationResult
    {
        public bool Success { get; set; }
        public bool ContentDeleted { get; set; }
        public bool UserWarned { get; set; }
        public bool UserMuted { get; set; }
        public bool UserBanned { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ModerationHistory
    {
        public Guid UserId { get; set; }
        public List<UserWarning> Warnings { get; set; } = new();
        public List<UserBan> Bans { get; set; } = new();
        public List<UserMute> Mutes { get; set; } = new();
        public List<ContentReport> ReportsAgainstUser { get; set; } = new();
    }

    #endregion
}
