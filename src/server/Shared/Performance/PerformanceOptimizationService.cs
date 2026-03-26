using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Shared.Performance;

/// <summary>
/// Database connection pool configuration and management
/// </summary>
public static class ConnectionPoolConfiguration
{
    /// <summary>
    /// Configure optimized database connection pooling
    /// </summary>
    public static IServiceCollection ConfigureDatabaseConnectionPooling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var poolConfig = configuration.GetSection("ConnectionPool");
        var maxPoolSize = poolConfig.GetValue("MaxPoolSize", 200);
        var minPoolSize = poolConfig.GetValue("MinPoolSize", 5);
        var connectionTimeout = poolConfig.GetValue("ConnectionTimeout", 30);
        var commandTimeout = poolConfig.GetValue("CommandTimeout", 30);
        var connectionLifetime = poolConfig.GetValue("ConnectionLifetime", 300);

        // Configure Npgsql connection pooling for PostgreSQL
        NpgsqlConnection.GlobalTypeMapper.UseJsonNet();

        // Connection string will be configured per service
        services.Configure<ConnectionPoolOptions>(options =>
        {
            options.MaxPoolSize = maxPoolSize;
            options.MinPoolSize = minPoolSize;
            options.ConnectionTimeout = connectionTimeout;
            options.CommandTimeout = commandTimeout;
            options.ConnectionLifetime = connectionLifetime;
        });

        return services;
    }

    /// <summary>
    /// Build optimized connection string for PostgreSQL
    /// </summary>
    public static string BuildOptimizedConnectionString(string baseConnectionString, ConnectionPoolOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            MaxPoolSize = options.MaxPoolSize,
            MinPoolSize = options.MinPoolSize,
            Timeout = options.ConnectionTimeout,
            CommandTimeout = options.CommandTimeout,
            ConnectionLifetime = options.ConnectionLifetime,
            // Performance optimizations
            NoResetOnClose = true, // Reuse connections without resetting state
            Pooling = true,
            Multiplexing = true, // Enable multiplexing for better throughput
            WriteBufferSize = 8192,
            ReadBufferSize = 8192
        };

        return builder.ConnectionString;
    }
}

/// <summary>
/// Connection pool options
/// </summary>
public class ConnectionPoolOptions
{
    public int MaxPoolSize { get; set; } = 200;
    public int MinPoolSize { get; set; } = 5;
    public int ConnectionTimeout { get; set; } = 30;
    public int CommandTimeout { get; set; } = 30;
    public int ConnectionLifetime { get; set; } = 300;
}

/// <summary>
/// Database index recommendations and management
/// </summary>
public class DatabaseIndexService
{
    private readonly DbContext _context;
    private readonly ILogger<DatabaseIndexService> _logger;

    public DatabaseIndexService(DbContext context, ILogger<DatabaseIndexService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get missing index recommendations
    /// </summary>
    public async Task<List<IndexRecommendation>> GetMissingIndexRecommendationsAsync()
    {
        var recommendations = new List<IndexRecommendation>();

        // Query PostgreSQL system tables for missing index suggestions
        var query = @"
            SELECT 
                schemaname,
                relname as tablename,
                attname as columnname,
                n_distinct,
                correlation
            FROM pg_stats
            WHERE schemaname = 'public'
            ORDER BY relname, attname";

        try
        {
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(1);
                var columnName = reader.GetString(2);
                var nDistinct = reader.GetDouble(3);

                // High cardinality columns are good candidates for indexing
                if (nDistinct > 0.1 || nDistinct < -0.1)
                {
                    recommendations.Add(new IndexRecommendation
                    {
                        TableName = tableName,
                        ColumnName = columnName,
                        Reason = "High cardinality column - good candidate for index",
                        Priority = Math.Abs(nDistinct) > 0.5 ? "High" : "Medium"
                    });
                }
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get index recommendations");
        }

        return recommendations;
    }

    /// <summary>
    /// Get unused indexes
    /// </summary>
    public async Task<List<UnusedIndex>> GetUnusedIndexesAsync()
    {
        var unusedIndexes = new List<UnusedIndex>();

        var query = @"
            SELECT 
                schemaname,
                relname as tablename,
                indexrelname as indexname,
                idx_scan as index_scans,
                pg_size_pretty(pg_relation_size(indexrelid)) as index_size
            FROM pg_stat_user_indexes
            WHERE idx_scan = 0
            AND schemaname = 'public'
            ORDER BY pg_relation_size(indexrelid) DESC";

        try
        {
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                unusedIndexes.Add(new UnusedIndex
                {
                    TableName = reader.GetString(1),
                    IndexName = reader.GetString(2),
                    IndexScans = reader.GetInt64(3),
                    IndexSize = reader.GetString(4)
                });
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unused indexes");
        }

        return unusedIndexes;
    }

    /// <summary>
    /// Create recommended index
    /// </summary>
    public async Task<bool> CreateIndexAsync(string tableName, string columnName, bool isUnique = false)
    {
        var indexName = $"idx_{tableName}_{columnName}";
        var uniqueClause = isUnique ? "UNIQUE" : "";
        var query = $"CREATE {uniqueClause} INDEX CONCURRENTLY IF NOT EXISTS {indexName} ON {tableName} ({columnName})";

        try
        {
            await _context.Database.ExecuteSqlRawAsync(query);
            _logger.LogInformation("Created index {IndexName} on {TableName}.{ColumnName}", 
                indexName, tableName, columnName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create index {IndexName}", indexName);
            return false;
        }
    }

    /// <summary>
    /// Drop unused index
    /// </summary>
    public async Task<bool> DropIndexAsync(string indexName)
    {
        var query = $"DROP INDEX CONCURRENTLY IF EXISTS {indexName}";

        try
        {
            await _context.Database.ExecuteSqlRawAsync(query);
            _logger.LogInformation("Dropped index {IndexName}", indexName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drop index {IndexName}", indexName);
            return false;
        }
    }
}

/// <summary>
/// Index recommendation
/// </summary>
public class IndexRecommendation
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
}

/// <summary>
/// Unused index information
/// </summary>
public class UnusedIndex
{
    public string TableName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public long IndexScans { get; set; }
    public string IndexSize { get; set; } = string.Empty;
}

/// <summary>
/// Query performance monitoring and analysis
/// </summary>
public class QueryPerformanceService
{
    private readonly DbContext _context;
    private readonly ILogger<QueryPerformanceService> _logger;
    private readonly ConcurrentDictionary<string, QueryStats> _queryStats = new();

    public QueryPerformanceService(DbContext context, ILogger<QueryPerformanceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get slow queries from PostgreSQL
    /// </summary>
    public async Task<List<SlowQuery>> GetSlowQueriesAsync(int minDurationMs = 1000)
    {
        var slowQueries = new List<SlowQuery>();

        var query = @"
            SELECT 
                calls,
                total_exec_time as total_time,
                mean_exec_time as mean_time,
                max_exec_time as max_time,
                query
            FROM pg_stat_statements
            WHERE mean_exec_time > @minDuration
            ORDER BY total_exec_time DESC
            LIMIT 50";

        try
        {
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            var param = command.CreateParameter();
            param.ParameterName = "@minDuration";
            param.Value = minDurationMs;
            command.Parameters.Add(param);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                slowQueries.Add(new SlowQuery
                {
                    Calls = reader.GetInt64(0),
                    TotalTimeMs = reader.GetDouble(1),
                    MeanTimeMs = reader.GetDouble(2),
                    MaxTimeMs = reader.GetDouble(3),
                    Query = reader.GetString(4)
                });
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get slow queries");
        }

        return slowQueries;
    }

    /// <summary>
    /// Track query execution time
    /// </summary>
    public void TrackQuery(string queryName, long durationMs)
    {
        var stats = _queryStats.GetOrAdd(queryName, _ => new QueryStats { QueryName = queryName });
        stats.RecordExecution(durationMs);
    }

    /// <summary>
    /// Get query statistics
    /// </summary>
    public Dictionary<string, QueryStats> GetQueryStatistics()
    {
        return new Dictionary<string, QueryStats>(_queryStats);
    }

    /// <summary>
    /// Reset statistics
    /// </summary>
    public void ResetStatistics()
    {
        _queryStats.Clear();
    }
}

/// <summary>
/// Query statistics
/// </summary>
public class QueryStats
{
    public string QueryName { get; set; } = string.Empty;
    public long TotalExecutions { get; set; }
    public long TotalTimeMs { get; set; }
    public long MinTimeMs { get; set; } = long.MaxValue;
    public long MaxTimeMs { get; set; }
    public double AverageTimeMs => TotalExecutions > 0 ? (double)TotalTimeMs / TotalExecutions : 0;

    public void RecordExecution(long durationMs)
    {
        Interlocked.Increment(ref TotalExecutions);
        Interlocked.Add(ref TotalTimeMs, durationMs);

        // Thread-safe min/max updates
        long currentMin;
        do
        {
            currentMin = MinTimeMs;
            if (durationMs >= currentMin) break;
        } while (Interlocked.CompareExchange(ref MinTimeMs, durationMs, currentMin) != currentMin);

        long currentMax;
        do
        {
            currentMax = MaxTimeMs;
            if (durationMs <= currentMax) break;
        } while (Interlocked.CompareExchange(ref MaxTimeMs, durationMs, currentMax) != currentMax);
    }
}

/// <summary>
/// Slow query information
/// </summary>
public class SlowQuery
{
    public long Calls { get; set; }
    public double TotalTimeMs { get; set; }
    public double MeanTimeMs { get; set; }
    public double MaxTimeMs { get; set; }
    public string Query { get; set; } = string.Empty;
}

/// <summary>
/// Memory cache with size limits and expiration
/// </summary>
public class PerformanceCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();
    private readonly long _maxSizeBytes;
    private readonly TimeSpan _defaultExpiration;
    private long _currentSizeBytes;
    private readonly ILogger? _logger;

    public PerformanceCache(long maxSizeBytes, TimeSpan defaultExpiration, ILogger? logger = null)
    {
        _maxSizeBytes = maxSizeBytes;
        _defaultExpiration = defaultExpiration;
        _logger = logger;
    }

    /// <summary>
    /// Get or add value to cache
    /// </summary>
    public async Task<TValue?> GetOrAddAsync(
        TKey key,
        Func<TKey, Task<TValue>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        // Check if exists and not expired
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.Expiration > DateTime.UtcNow)
            {
                entry.LastAccess = DateTime.UtcNow;
                return entry.Value;
            }

            // Remove expired entry
            RemoveEntry(key, entry);
        }

        // Create new entry
        var value = await factory(key);
        if (value == null) return default;

        var newEntry = new CacheEntry
        {
            Value = value,
            Created = DateTime.UtcNow,
            LastAccess = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.Add(expiration ?? _defaultExpiration),
            Size = EstimateSize(value)
        };

        // Check if we need to evict entries
        while (_currentSizeBytes + newEntry.Size > _maxSizeBytes && _cache.Count > 0)
        {
            EvictOldestEntry();
        }

        if (_cache.TryAdd(key, newEntry))
        {
            Interlocked.Add(ref _currentSizeBytes, newEntry.Size);
        }

        return value;
    }

    /// <summary>
    /// Remove entry from cache
    /// </summary>
    public bool Remove(TKey key)
    {
        if (_cache.TryRemove(key, out var entry))
        {
            RemoveEntry(key, entry);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clear cache
    /// </summary>
    public void Clear()
    {
        foreach (var key in _cache.Keys)
        {
            if (_cache.TryRemove(key, out var entry))
            {
                Interlocked.Add(ref _currentSizeBytes, -entry.Size);
            }
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            EntryCount = _cache.Count,
            CurrentSizeBytes = _currentSizeBytes,
            MaxSizeBytes = _maxSizeBytes
        };
    }

    private void RemoveEntry(TKey key, CacheEntry entry)
    {
        _cache.TryRemove(key, out _);
        Interlocked.Add(ref _currentSizeBytes, -entry.Size);
    }

    private void EvictOldestEntry()
    {
        var oldest = _cache.OrderBy(x => x.Value.LastAccess).FirstOrDefault();
        if (!oldest.Equals(default(KeyValuePair<TKey, CacheEntry>)))
        {
            RemoveEntry(oldest.Key, oldest.Value);
            _logger?.LogDebug("Evicted cache entry {Key} due to size limit", oldest.Key);
        }
    }

    private static long EstimateSize(TValue value)
    {
        // Simple estimation based on type
        if (value is string str)
            return str.Length * 2; // Unicode chars
        if (value is byte[] bytes)
            return bytes.Length;
        if (value is Array array)
            return array.Length * 100; // Rough estimate
        
        // Default estimate
        return 1000;
    }

    private class CacheEntry
    {
        public TValue Value { get; set; } = default!;
        public DateTime Created { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime Expiration { get; set; }
        public long Size { get; set; }
    }
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public int EntryCount { get; set; }
    public long CurrentSizeBytes { get; set; }
    public long MaxSizeBytes { get; set; }
    public double FillPercentage => MaxSizeBytes > 0 ? (double)CurrentSizeBytes / MaxSizeBytes * 100 : 0;
}

/// <summary>
/// Async locking mechanism for concurrent operations
/// </summary>
public class AsyncLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Task<IDisposable> _releaser;

    public AsyncLock()
    {
        _releaser = Task.FromResult((IDisposable)new Releaser(_semaphore));
    }

    public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        var wait = _semaphore.WaitAsync(cancellationToken);
        return wait.IsCompleted
            ? _releaser
            : wait.ContinueWith(
                (_, releaser) => (IDisposable)releaser!,
                _releaser.Result,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
        }
    }
}

/// <summary>
/// Batch processing helper for efficient bulk operations
/// </summary>
public static class BatchProcessor
{
    /// <summary>
    /// Process items in batches
    /// </summary>
    public static async Task<List<TOutput>> ProcessBatchAsync<TInput, TOutput>(
        IEnumerable<TInput> items,
        Func<List<TInput>, CancellationToken, Task<List<TOutput>>> processor,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TOutput>();
        var batch = new List<TInput>(batchSize);

        foreach (var item in items)
        {
            batch.Add(item);

            if (batch.Count >= batchSize)
            {
                var batchResults = await processor(batch, cancellationToken);
                results.AddRange(batchResults);
                batch.Clear();
            }
        }

        // Process remaining items
        if (batch.Count > 0)
        {
            var batchResults = await processor(batch, cancellationToken);
            results.AddRange(batchResults);
        }

        return results;
    }

    /// <summary>
    /// Process items in parallel batches
    /// </summary>
    public static async Task<List<TOutput>> ProcessParallelBatchAsync<TInput, TOutput>(
        IEnumerable<TInput> items,
        Func<List<TInput>, CancellationToken, Task<List<TOutput>>> processor,
        int batchSize = 100,
        int maxDegreeOfParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        var batches = items
            .Select((item, index) => new { item, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.item).ToList())
            .ToList();

        var results = new List<TOutput>();
        var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

        var tasks = batches.Select(async batch =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await processor(batch, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var batchResults = await Task.WhenAll(tasks);
        results.AddRange(batchResults.SelectMany(r => r));

        return results;
    }
}

/// <summary>
/// Configuration extension methods
/// </summary>
public static class PerformanceExtensions
{
    /// <summary>
    /// Add performance monitoring services
    /// </summary>
    public static IServiceCollection AddPerformanceMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ConnectionPoolOptions>(configuration.GetSection("ConnectionPool"));
        services.AddScoped<DatabaseIndexService>();
        services.AddScoped<QueryPerformanceService>();

        return services;
    }
}
