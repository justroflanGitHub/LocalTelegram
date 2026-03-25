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
    /// Data retention policy management service
    /// </summary>
    public class DataRetentionService
    {
        private readonly AdminDbContext _context;
        private readonly ILogger<DataRetentionService> _logger;

        public DataRetentionService(AdminDbContext context, ILogger<DataRetentionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Creates or updates a data retention policy
        /// </summary>
        public async Task<DataRetentionPolicy> CreateOrUpdatePolicyAsync(
            string name, 
            DataType dataType, 
            int retentionDays, 
            bool autoDelete = false,
            string? description = null)
        {
            var policy = await _context.DataRetentionPolicies
                .FirstOrDefaultAsync(p => p.Name == name);

            if (policy == null)
            {
                policy = new DataRetentionPolicy
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    CreatedAt = DateTime.UtcNow
                };
                _context.DataRetentionPolicies.Add(policy);
            }

            policy.DataType = dataType;
            policy.RetentionDays = retentionDays;
            policy.AutoDelete = autoDelete;
            policy.Description = description;
            policy.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Data retention policy '{Name}' created/updated: {RetentionDays} days, AutoDelete: {AutoDelete}", 
                name, retentionDays, autoDelete);

            return policy;
        }

        /// <summary>
        /// Gets all data retention policies
        /// </summary>
        public async Task<List<DataRetentionPolicy>> GetPoliciesAsync()
        {
            return await _context.DataRetentionPolicies
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        /// <summary>
        /// Gets a specific policy by data type
        /// </summary>
        public async Task<DataRetentionPolicy?> GetPolicyByDataTypeAsync(DataType dataType)
        {
            return await _context.DataRetentionPolicies
                .FirstOrDefaultAsync(p => p.DataType == dataType);
        }

        /// <summary>
        /// Deletes a data retention policy
        /// </summary>
        public async Task<bool> DeletePolicyAsync(Guid policyId)
        {
            var policy = await _context.DataRetentionPolicies.FindAsync(policyId);
            if (policy == null)
            {
                return false;
            }

            _context.DataRetentionPolicies.Remove(policy);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Data retention policy '{Name}' deleted", policy.Name);

            return true;
        }

        /// <summary>
        /// Executes data retention policies (typically run as a scheduled job)
        /// </summary>
        public async Task<RetentionExecutionResult> ExecuteRetentionPoliciesAsync()
        {
            var result = new RetentionExecutionResult
            {
                ExecutedAt = DateTime.UtcNow
            };

            var policies = await _context.DataRetentionPolicies
                .Where(p => p.AutoDelete)
                .ToListAsync();

            foreach (var policy in policies)
            {
                try
                {
                    var deletedCount = await ExecutePolicyAsync(policy);
                    result.PoliciesExecuted++;
                    result.TotalRecordsDeleted += deletedCount;
                    result.PolicyResults.Add(new PolicyExecutionResult
                    {
                        PolicyName = policy.Name,
                        DataType = policy.DataType,
                        RecordsDeleted = deletedCount,
                        Success = true
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to execute retention policy '{PolicyName}'", policy.Name);
                    result.PolicyResults.Add(new PolicyExecutionResult
                    {
                        PolicyName = policy.Name,
                        DataType = policy.DataType,
                        RecordsDeleted = 0,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            _logger.LogInformation("Executed {Count} retention policies, deleted {Total} records", 
                result.PoliciesExecuted, result.TotalRecordsDeleted);

            return result;
        }

        /// <summary>
        /// Executes a single retention policy
        /// </summary>
        private async Task<int> ExecutePolicyAsync(DataRetentionPolicy policy)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-policy.RetentionDays);

            return policy.DataType switch
            {
                DataType.Messages => await DeleteOldMessagesAsync(cutoffDate),
                DataType.Files => await DeleteOldFilesAsync(cutoffDate),
                DataType.AuditLogs => await DeleteOldAuditLogsAsync(cutoffDate),
                DataType.Sessions => await DeleteOldSessionsAsync(cutoffDate),
                DataType.Notifications => await DeleteOldNotificationsAsync(cutoffDate),
                DataType.MediaFiles => await DeleteOldMediaFilesAsync(cutoffDate),
                DataType.CallRecords => await DeleteOldCallRecordsAsync(cutoffDate),
                _ => 0
            };
        }

        /// <summary>
        /// Gets retention statistics
        /// </summary>
        public async Task<RetentionStatistics> GetStatisticsAsync()
        {
            var policies = await _context.DataRetentionPolicies.ToListAsync();

            return new RetentionStatistics
            {
                TotalPolicies = policies.Count,
                AutoDeleteEnabled = policies.Count(p => p.AutoDelete),
                PoliciesByDataType = policies
                    .GroupBy(p => p.DataType)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AverageRetentionDays = policies.Any() 
                    ? (int)policies.Average(p => p.RetentionDays) 
                    : 0,
                LastExecution = await GetLastExecutionTimeAsync()
            };
        }

        /// <summary>
        /// Previews what would be deleted by a policy
        /// </summary>
        public async Task<RetentionPreview> PreviewDeletionAsync(DataType dataType, int retentionDays)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            return new RetentionPreview
            {
                DataType = dataType,
                CutoffDate = cutoffDate,
                RecordsToDelete = dataType switch
                {
                    DataType.Messages => await CountOldMessagesAsync(cutoffDate),
                    DataType.Files => await CountOldFilesAsync(cutoffDate),
                    DataType.AuditLogs => await CountOldAuditLogsAsync(cutoffDate),
                    DataType.Sessions => await CountOldSessionsAsync(cutoffDate),
                    DataType.Notifications => await CountOldNotificationsAsync(cutoffDate),
                    DataType.MediaFiles => await CountOldMediaFilesAsync(cutoffDate),
                    DataType.CallRecords => await CountOldCallRecordsAsync(cutoffDate),
                    _ => 0
                }
            };
        }

        #region Private Delete Methods

        private async Task<int> DeleteOldMessagesAsync(DateTime cutoffDate)
        {
            // In production, this would call MessageService API
            // For now, return placeholder
            _logger.LogInformation("Deleting messages older than {CutoffDate}", cutoffDate);
            return await Task.FromResult(0);
        }

        private async Task<int> DeleteOldFilesAsync(DateTime cutoffDate)
        {
            // In production, this would call FileService API
            _logger.LogInformation("Deleting files older than {CutoffDate}", cutoffDate);
            return await Task.FromResult(0);
        }

        private async Task<int> DeleteOldAuditLogsAsync(DateTime cutoffDate)
        {
            var logs = await _context.AuditLogs
                .Where(l => l.Timestamp < cutoffDate)
                .ToListAsync();

            _context.AuditLogs.RemoveRange(logs);
            await _context.SaveChangesAsync();

            return logs.Count;
        }

        private async Task<int> DeleteOldSessionsAsync(DateTime cutoffDate)
        {
            // In production, this would call AuthService API
            _logger.LogInformation("Deleting sessions older than {CutoffDate}", cutoffDate);
            return await Task.FromResult(0);
        }

        private async Task<int> DeleteOldNotificationsAsync(DateTime cutoffDate)
        {
            // In production, this would call PushService API
            _logger.LogInformation("Deleting notifications older than {CutoffDate}", cutoffDate);
            return await Task.FromResult(0);
        }

        private async Task<int> DeleteOldMediaFilesAsync(DateTime cutoffDate)
        {
            // In production, this would call MediaService API
            _logger.LogInformation("Deleting media files older than {CutoffDate}", cutoffDate);
            return await Task.FromResult(0);
        }

        private async Task<int> DeleteOldCallRecordsAsync(DateTime cutoffDate)
        {
            // In production, this would call ConferenceService API
            _logger.LogInformation("Deleting call records older than {CutoffDate}", cutoffDate);
            return await Task.FromResult(0);
        }

        #endregion

        #region Private Count Methods

        private async Task<long> CountOldMessagesAsync(DateTime cutoffDate)
        {
            return await Task.FromResult(0L);
        }

        private async Task<long> CountOldFilesAsync(DateTime cutoffDate)
        {
            return await Task.FromResult(0L);
        }

        private async Task<long> CountOldAuditLogsAsync(DateTime cutoffDate)
        {
            return await _context.AuditLogs
                .LongCountAsync(l => l.Timestamp < cutoffDate);
        }

        private async Task<long> CountOldSessionsAsync(DateTime cutoffDate)
        {
            return await Task.FromResult(0L);
        }

        private async Task<long> CountOldNotificationsAsync(DateTime cutoffDate)
        {
            return await Task.FromResult(0L);
        }

        private async Task<long> CountOldMediaFilesAsync(DateTime cutoffDate)
        {
            return await Task.FromResult(0L);
        }

        private async Task<long> CountOldCallRecordsAsync(DateTime cutoffDate)
        {
            return await Task.FromResult(0L);
        }

        private async Task<DateTime?> GetLastExecutionTimeAsync()
        {
            // This would typically come from a job execution log
            return await Task.FromResult<DateTime?>(null);
        }

        #endregion
    }

    #region Models

    public class DataRetentionPolicy
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DataType DataType { get; set; }
        public int RetentionDays { get; set; }
        public bool AutoDelete { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public enum DataType
    {
        Messages,
        Files,
        AuditLogs,
        Sessions,
        Notifications,
        MediaFiles,
        CallRecords,
        UserProfiles,
        GroupData
    }

    public class RetentionExecutionResult
    {
        public DateTime ExecutedAt { get; set; }
        public int PoliciesExecuted { get; set; }
        public int TotalRecordsDeleted { get; set; }
        public List<PolicyExecutionResult> PolicyResults { get; set; } = new();
    }

    public class PolicyExecutionResult
    {
        public string PolicyName { get; set; } = string.Empty;
        public DataType DataType { get; set; }
        public int RecordsDeleted { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class RetentionStatistics
    {
        public int TotalPolicies { get; set; }
        public int AutoDeleteEnabled { get; set; }
        public Dictionary<DataType, int> PoliciesByDataType { get; set; } = new();
        public int AverageRetentionDays { get; set; }
        public DateTime? LastExecution { get; set; }
    }

    public class RetentionPreview
    {
        public DataType DataType { get; set; }
        public DateTime CutoffDate { get; set; }
        public long RecordsToDelete { get; set; }
    }

    #endregion
}
