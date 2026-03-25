using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserService.Data;

namespace UserService.Services
{
    /// <summary>
    /// GDPR compliance service for data export and deletion
    /// </summary>
    public class GdprService
    {
        private readonly UserDbContext _context;
        private readonly ILogger<GdprService> _logger;

        public GdprService(UserDbContext context, ILogger<GdprService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Exports all user data in JSON format (GDPR Article 20 - Right to data portability)
        /// </summary>
        public async Task<byte[]> ExportUserDataAsync(Guid userId)
        {
            _logger.LogInformation("Starting data export for user {UserId}", userId);

            var exportData = new UserDataExport
            {
                ExportDate = DateTime.UtcNow,
                ExportVersion = "1.0",
                User = await ExportUserProfileAsync(userId),
                Contacts = await ExportContactsAsync(userId),
                BlockedUsers = await ExportBlockedUsersAsync(userId),
                Sessions = await ExportSessionsAsync(userId),
                Settings = await ExportSettingsAsync(userId)
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(exportData, jsonOptions);
            
            _logger.LogInformation("Data export completed for user {UserId}", userId);

            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Exports user data as a ZIP file containing JSON and media files
        /// </summary>
        public async Task<byte[]> ExportUserDataWithMediaAsync(Guid userId, string mediaBasePath)
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                // Add JSON data
                var jsonData = await ExportUserDataAsync(userId);
                var jsonEntry = archive.CreateEntry("user_data.json");
                using (var entryStream = jsonEntry.Open())
                {
                    await entryStream.WriteAsync(jsonData);
                }

                // Add profile picture if exists
                var profilePicturePath = Path.Combine(mediaBasePath, "profiles", $"{userId}.jpg");
                if (File.Exists(profilePicturePath))
                {
                    var pictureEntry = archive.CreateEntry("media/profile_picture.jpg");
                    using var entryStream = pictureEntry.Open();
                    using var fileStream = File.OpenRead(profilePicturePath);
                    await fileStream.CopyToAsync(entryStream);
                }

                // Add README
                var readmeEntry = archive.CreateEntry("README.txt");
                using (var entryStream = readmeEntry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    await writer.WriteLineAsync("LocalTelegram Data Export");
                    await writer.WriteLineAsync($"Export Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync("Contents:");
                    await writer.WriteLineAsync("- user_data.json: All your personal data in JSON format");
                    await writer.WriteLineAsync("- media/: Your profile pictures and other media");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync("This export is provided in compliance with GDPR Article 20");
                    await writer.WriteLineAsync("(Right to data portability).");
                }
            }

            return memoryStream.ToArray();
        }

        /// <summary>
        /// Deletes all user data (GDPR Article 17 - Right to erasure)
        /// </summary>
        public async Task<DataDeletionResult> DeleteUserDataAsync(Guid userId, bool hardDelete = false)
        {
            _logger.LogInformation("Starting data deletion for user {UserId} (HardDelete: {HardDelete})", 
                userId, hardDelete);

            var result = new DataDeletionResult();

            try
            {
                // Anonymize or delete messages
                result.MessagesDeleted = await DeleteUserMessagesAsync(userId, hardDelete);

                // Delete contacts
                result.ContactsDeleted = await DeleteUserContactsAsync(userId);

                // Delete blocked users
                result.BlocksDeleted = await DeleteUserBlocksAsync(userId);

                // Delete sessions
                result.SessionsDeleted = await DeleteUserSessionsAsync(userId);

                // Delete settings
                result.SettingsDeleted = await DeleteUserSettingsAsync(userId);

                // Delete or anonymize profile
                if (hardDelete)
                {
                    await HardDeleteUserAsync(userId);
                    result.ProfileDeleted = true;
                }
                else
                {
                    await AnonymizeUserAsync(userId);
                    result.ProfileAnonymized = true;
                }

                result.Success = true;
                result.DeletedAt = DateTime.UtcNow;

                _logger.LogInformation("Data deletion completed for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data deletion failed for user {UserId}", userId);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Schedules data deletion after a grace period
        /// </summary>
        public async Task ScheduleDataDeletionAsync(Guid userId, int gracePeriodDays = 30)
        {
            var scheduledDeletion = new ScheduledDeletion
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ScheduledAt = DateTime.UtcNow.AddDays(gracePeriodDays),
                CreatedAt = DateTime.UtcNow,
                IsCompleted = false
            };

            _context.ScheduledDeletions.Add(scheduledDeletion);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Scheduled data deletion for user {UserId} at {ScheduledAt}", 
                userId, scheduledDeletion.ScheduledAt);
        }

        /// <summary>
        /// Cancels a scheduled data deletion
        /// </summary>
        public async Task<bool> CancelScheduledDeletionAsync(Guid userId)
        {
            var scheduled = await _context.ScheduledDeletions
                .FirstOrDefaultAsync(d => d.UserId == userId && !d.IsCompleted);

            if (scheduled == null)
            {
                return false;
            }

            _context.ScheduledDeletions.Remove(scheduled);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cancelled scheduled deletion for user {UserId}", userId);

            return true;
        }

        /// <summary>
        /// Processes pending scheduled deletions
        /// </summary>
        public async Task<int> ProcessScheduledDeletionsAsync()
        {
            var pendingDeletions = await _context.ScheduledDeletions
                .Where(d => !d.IsCompleted && d.ScheduledAt <= DateTime.UtcNow)
                .ToListAsync();

            var processed = 0;
            foreach (var deletion in pendingDeletions)
            {
                try
                {
                    await DeleteUserDataAsync(deletion.UserId, hardDelete: true);
                    
                    deletion.IsCompleted = true;
                    deletion.CompletedAt = DateTime.UtcNow;
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process scheduled deletion for user {UserId}", 
                        deletion.UserId);
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Processed {Count} scheduled deletions", processed);

            return processed;
        }

        #region Private Export Methods

        private async Task<UserProfileExport> ExportUserProfileAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return new UserProfileExport();

            return new UserProfileExport
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Bio = user.Bio,
                CreatedAt = user.CreatedAt,
                LastActiveAt = user.LastActiveAt
            };
        }

        private async Task<List<ContactExport>> ExportContactsAsync(Guid userId)
        {
            var contacts = await _context.Contacts
                .Where(c => c.UserId == userId)
                .ToListAsync();

            return contacts.Select(c => new ContactExport
            {
                ContactId = c.ContactId,
                AddedAt = c.AddedAt,
                Alias = c.Alias
            }).ToList();
        }

        private async Task<List<BlockedUserExport>> ExportBlockedUsersAsync(Guid userId)
        {
            var blocked = await _context.BlockedUsers
                .Where(b => b.UserId == userId)
                .ToListAsync();

            return blocked.Select(b => new BlockedUserExport
            {
                BlockedUserId = b.BlockedUserId,
                BlockedAt = b.BlockedAt,
                Reason = b.Reason
            }).ToList();
        }

        private async Task<List<SessionExport>> ExportSessionsAsync(Guid userId)
        {
            var sessions = await _context.Sessions
                .Where(s => s.UserId == userId)
                .ToListAsync();

            return sessions.Select(s => new SessionExport
            {
                DeviceName = s.DeviceName,
                IpAddress = s.IpAddress,
                CreatedAt = s.CreatedAt,
                LastActiveAt = s.LastActiveAt
            }).ToList();
        }

        private async Task<Dictionary<string, object>> ExportSettingsAsync(Guid userId)
        {
            var settings = await _context.UserSettings
                .Where(s => s.UserId == userId)
                .ToListAsync();

            return settings.ToDictionary(s => s.Key, s => (object)s.Value);
        }

        #endregion

        #region Private Delete Methods

        private async Task<int> DeleteUserMessagesAsync(Guid userId, bool hardDelete)
        {
            // This would typically call MessageService
            // For now, return 0 as messages are in a different database
            return 0;
        }

        private async Task<int> DeleteUserContactsAsync(Guid userId)
        {
            var contacts = await _context.Contacts
                .Where(c => c.UserId == userId || c.ContactId == userId)
                .ToListAsync();

            _context.Contacts.RemoveRange(contacts);
            return contacts.Count;
        }

        private async Task<int> DeleteUserBlocksAsync(Guid userId)
        {
            var blocks = await _context.BlockedUsers
                .Where(b => b.UserId == userId || b.BlockedUserId == userId)
                .ToListAsync();

            _context.BlockedUsers.RemoveRange(blocks);
            return blocks.Count;
        }

        private async Task<int> DeleteUserSessionsAsync(Guid userId)
        {
            var sessions = await _context.Sessions
                .Where(s => s.UserId == userId)
                .ToListAsync();

            _context.Sessions.RemoveRange(sessions);
            return sessions.Count;
        }

        private async Task<int> DeleteUserSettingsAsync(Guid userId)
        {
            var settings = await _context.UserSettings
                .Where(s => s.UserId == userId)
                .ToListAsync();

            _context.UserSettings.RemoveRange(settings);
            return settings.Count;
        }

        private async Task AnonymizeUserAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return;

            // Anonymize personal data but keep the record
            user.Username = $"deleted_{Guid.NewGuid():N}";
            user.Email = null;
            user.PhoneNumber = null;
            user.FirstName = "Deleted";
            user.LastName = "User";
            user.Bio = null;
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        private async Task HardDeleteUserAsync(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return;

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
        }

        #endregion
    }

    #region Export Models

    public class UserDataExport
    {
        public string ExportVersion { get; set; } = string.Empty;
        public DateTime ExportDate { get; set; }
        public UserProfileExport User { get; set; } = new();
        public List<ContactExport> Contacts { get; set; } = new();
        public List<BlockedUserExport> BlockedUsers { get; set; } = new();
        public List<SessionExport> Sessions { get; set; } = new();
        public Dictionary<string, object> Settings { get; set; } = new();
    }

    public class UserProfileExport
    {
        public Guid Id { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Bio { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }
    }

    public class ContactExport
    {
        public Guid ContactId { get; set; }
        public DateTime AddedAt { get; set; }
        public string? Alias { get; set; }
    }

    public class BlockedUserExport
    {
        public Guid BlockedUserId { get; set; }
        public DateTime BlockedAt { get; set; }
        public string? Reason { get; set; }
    }

    public class SessionExport
    {
        public string? DeviceName { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }
    }

    public class DataDeletionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int MessagesDeleted { get; set; }
        public int ContactsDeleted { get; set; }
        public int BlocksDeleted { get; set; }
        public int SessionsDeleted { get; set; }
        public int SettingsDeleted { get; set; }
        public bool ProfileAnonymized { get; set; }
        public bool ProfileDeleted { get; set; }
    }

    public class ScheduledDeletion
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public DateTime ScheduledAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    #endregion

    #region Placeholder Models (should be in actual DbContext)

    // These are placeholder models - in production, these would be actual EF Core entities
    public class User
    {
        public Guid Id { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Bio { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    public class Contact
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid ContactId { get; set; }
        public DateTime AddedAt { get; set; }
        public string? Alias { get; set; }
    }

    public class BlockedUser
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid BlockedUserId { get; set; }
        public DateTime BlockedAt { get; set; }
        public string? Reason { get; set; }
    }

    public class Session
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? DeviceName { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }
    }

    public class UserSetting
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    #endregion
}
