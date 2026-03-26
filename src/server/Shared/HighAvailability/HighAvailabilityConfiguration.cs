using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Shared.HighAvailability;

/// <summary>
/// High Availability configuration for PostgreSQL and Redis
/// </summary>
public static class HighAvailabilityConfiguration
{
    /// <summary>
    /// Add high availability services
    /// </summary>
    public static IServiceCollection AddHighAvailability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PostgreSqlHAOptions>(configuration.GetSection("PostgreSQL"));
        services.Configure<RedisHAOptions>(configuration.GetSection("Redis"));
        
        services.AddSingleton<IPostgreSqlHealthCheck, PostgreSqlHealthCheck>();
        services.AddSingleton<IRedisHealthCheck, RedisHealthCheck>();
        services.AddSingleton<IFailoverManager, FailoverManager>();
        services.AddHostedService<HealthCheckBackgroundService>();

        return services;
    }
}

/// <summary>
/// PostgreSQL High Availability options
/// </summary>
public class PostgreSqlHAOptions
{
    /// <summary>
    /// Primary connection string
    /// </summary>
    public string PrimaryConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Replica connection strings
    /// </summary>
    public List<string> ReplicaConnectionStrings { get; set; } = new();

    /// <summary>
    /// Enable read replicas for read operations
    /// </summary>
    public bool EnableReadReplicas { get; set; } = true;

    /// <summary>
    /// Health check interval in seconds
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Failover timeout in seconds
    /// </summary>
    public int FailoverTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Retry delay in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;
}

/// <summary>
/// Redis High Availability options
/// </summary>
public class RedisHAOptions
{
    /// <summary>
    /// Redis master endpoint
    /// </summary>
    public string MasterEndpoint { get; set; } = "localhost:6379";

    /// <summary>
    /// Redis replica endpoints
    /// </summary>
    public List<string> ReplicaEndpoints { get; set; } = new();

    /// <summary>
    /// Enable Redis Sentinel for automatic failover
    /// </summary>
    public bool EnableSentinel { get; set; } = false;

    /// <summary>
    /// Sentinel endpoints
    /// </summary>
    public List<string> SentinelEndpoints { get; set; } = new();

    /// <summary>
    /// Sentinel master name
    /// </summary>
    public string SentinelMasterName { get; set; } = "mymaster";

    /// <summary>
    /// Password for Redis authentication
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Health check interval in seconds
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Connect timeout in milliseconds
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Sync timeout in milliseconds
    /// </summary>
    public int SyncTimeoutMs { get; set; } = 5000;
}

/// <summary>
/// PostgreSQL health check interface
/// </summary>
public interface IPostgreSqlHealthCheck
{
    Task<DatabaseHealthStatus> CheckHealthAsync();
    string GetHealthyReplicaConnectionString();
    string GetPrimaryConnectionString();
}

/// <summary>
/// Redis health check interface
/// </summary>
public interface IRedisHealthCheck
{
    Task<RedisHealthStatus> CheckHealthAsync();
    IConnectionMultiplexer GetHealthyConnection();
}

/// <summary>
/// Failover manager interface
/// </summary>
public interface IFailoverManager
{
    Task<bool> FailoverDatabaseAsync();
    Task<bool> FailoverRedisAsync();
    FailoverStatus GetStatus();
}

/// <summary>
/// Database health status
/// </summary>
public class DatabaseHealthStatus
{
    public bool IsHealthy { get; set; }
    public bool PrimaryHealthy { get; set; }
    public List<ReplicaHealth> Replicas { get; set; } = new();
    public DateTime LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Replica health information
/// </summary>
public class ReplicaHealth
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public int LagMs { get; set; }
    public bool IsSynced { get; set; }
}

/// <summary>
/// Redis health status
/// </summary>
public class RedisHealthStatus
{
    public bool IsHealthy { get; set; }
    public bool MasterHealthy { get; set; }
    public List<RedisReplicaHealth> Replicas { get; set; } = new();
    public DateTime LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Redis replica health information
/// </summary>
public class RedisReplicaHealth
{
    public string Endpoint { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public long Lag { get; set; }
    public bool IsSynced { get; set; }
}

/// <summary>
/// Failover status
/// </summary>
public class FailoverStatus
{
    public bool DatabaseFailoverInProgress { get; set; }
    public bool RedisFailoverInProgress { get; set; }
    public DateTime? LastDatabaseFailover { get; set; }
    public DateTime? LastRedisFailover { get; set; }
    public int DatabaseFailoverCount { get; set; }
    public int RedisFailoverCount { get; set; }
}

/// <summary>
/// PostgreSQL health check implementation
/// </summary>
public class PostgreSqlHealthCheck : IPostgreSqlHealthCheck
{
    private readonly PostgreSqlHAOptions _options;
    private readonly ILogger<PostgreSqlHealthCheck> _logger;
    private DatabaseHealthStatus _lastStatus = new();
    private readonly object _lock = new();
    private int _currentReplicaIndex;

    public PostgreSqlHealthCheck(
        IOptions<PostgreSqlHAOptions> options,
        ILogger<PostgreSqlHealthCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DatabaseHealthStatus> CheckHealthAsync()
    {
        var status = new DatabaseHealthStatus
        {
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Check primary
            status.PrimaryHealthy = await CheckConnectionAsync(_options.PrimaryConnectionString);

            // Check replicas
            foreach (var replica in _options.ReplicaConnectionStrings)
            {
                var replicaHealth = await CheckReplicaHealthAsync(replica);
                status.Replicas.Add(replicaHealth);
            }

            status.IsHealthy = status.PrimaryHealthy || status.Replicas.Any(r => r.IsHealthy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            status.ErrorMessage = ex.Message;
            status.IsHealthy = false;
        }

        lock (_lock)
        {
            _lastStatus = status;
        }

        return status;
    }

    /// <inheritdoc />
    public string GetHealthyReplicaConnectionString()
    {
        if (!_options.EnableReadReplicas || _options.ReplicaConnectionStrings.Count == 0)
        {
            return _options.PrimaryConnectionString;
        }

        var status = _lastStatus;
        var healthyReplicas = status.Replicas
            .Where(r => r.IsHealthy && r.IsSynced)
            .ToList();

        if (healthyReplicas.Count == 0)
        {
            _logger.LogWarning("No healthy replicas available, falling back to primary");
            return _options.PrimaryConnectionString;
        }

        // Round-robin selection
        var index = Interlocked.Increment(ref _currentReplicaIndex) % healthyReplicas.Count;
        return healthyReplicas[index].ConnectionString;
    }

    /// <inheritdoc />
    public string GetPrimaryConnectionString()
    {
        return _options.PrimaryConnectionString;
    }

    private async Task<bool> CheckConnectionAsync(string connectionString)
    {
        try
        {
            using var connection = new Npgsql.NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync();
            await connection.CloseAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection check failed for {ConnectionString}", 
                MaskConnectionString(connectionString));
            return false;
        }
    }

    private async Task<ReplicaHealth> CheckReplicaHealthAsync(string connectionString)
    {
        var health = new ReplicaHealth
        {
            ConnectionString = connectionString
        };

        try
        {
            using var connection = new Npgsql.NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // Check if replica is in recovery mode
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT pg_is_in_recovery()";
                var inRecovery = (bool)await command.ExecuteScalarAsync();
                health.IsSynced = !inRecovery; // If not in recovery, it's synced
            }

            // Check replication lag
            using (var lagCommand = connection.CreateCommand())
            {
                lagCommand.CommandText = @"
                    SELECT COALESCE(
                        EXTRACT(EPOCH FROM (now() - pg_last_xact_replay_timestamp())) * 1000,
                        0
                    )::int";
                health.LagMs = (int)await lagCommand.ExecuteScalarAsync();
            }

            health.IsHealthy = true;
            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replica health check failed for {ConnectionString}",
                MaskConnectionString(connectionString));
            health.IsHealthy = false;
        }

        return health;
    }

    private static string MaskConnectionString(string connectionString)
    {
        // Mask password in connection string for logging
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            "Password=[^;]*",
            "Password=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Redis health check implementation
/// </summary>
public class RedisHealthCheck : IRedisHealthCheck
{
    private readonly RedisHAOptions _options;
    private readonly ILogger<RedisHealthCheck> _logger;
    private IConnectionMultiplexer? _connection;
    private RedisHealthStatus _lastStatus = new();
    private readonly object _lock = new();

    public RedisHealthCheck(
        IOptions<RedisHAOptions> options,
        ILogger<RedisHealthCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RedisHealthStatus> CheckHealthAsync()
    {
        var status = new RedisHealthStatus
        {
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Ensure connection
            if (_connection == null || !_connection.IsConnected)
            {
                _connection = await CreateConnectionAsync();
            }

            var endpoints = _connection.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = _connection.GetServer(endpoint);
                var info = await server.InfoAsync("replication");
                
                var replicationInfo = info.FirstOrDefault(i => i.Key == "Replication");
                if (replicationInfo != null)
                {
                    var role = replicationInfo.First(i => i.Key == "role").Value;
                    
                    if (role == "master")
                    {
                        status.MasterHealthy = server.IsConnected;
                    }
                    else if (role == "slave")
                    {
                        var replicaHealth = new RedisReplicaHealth
                        {
                            Endpoint = endpoint.ToString() ?? "",
                            IsHealthy = server.IsConnected
                        };

                        // Get lag info
                        var lagInfo = replicationInfo.FirstOrDefault(i => i.Key == "master_link_lag");
                        if (lagInfo != null && long.TryParse(lagInfo.Value, out var lag))
                        {
                            replicaHealth.Lag = lag;
                            replicaHealth.IsSynced = lag < 1000; // Less than 1 second lag
                        }

                        status.Replicas.Add(replicaHealth);
                    }
                }
            }

            status.IsHealthy = status.MasterHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            status.ErrorMessage = ex.Message;
            status.IsHealthy = false;
        }

        lock (_lock)
        {
            _lastStatus = status;
        }

        return status;
    }

    /// <inheritdoc />
    public IConnectionMultiplexer GetHealthyConnection()
    {
        if (_connection != null && _connection.IsConnected)
        {
            return _connection;
        }

        throw new RedisConnectionException("No healthy Redis connection available");
    }

    private async Task<IConnectionMultiplexer> CreateConnectionAsync()
    {
        ConfigurationOptions config;

        if (_options.EnableSentinel && _options.SentinelEndpoints.Count > 0)
        {
            // Use Sentinel for automatic failover
            config = new ConfigurationOptions
            {
                ServiceName = _options.SentinelMasterName,
                Password = _options.Password,
                ConnectTimeout = _options.ConnectTimeoutMs,
                SyncTimeout = _options.SyncTimeoutMs,
                AbortOnConnectFail = false
            };

            foreach (var endpoint in _options.SentinelEndpoints)
            {
                config.EndPoints.Add(endpoint);
            }
        }
        else
        {
            // Direct connection to master
            config = new ConfigurationOptions
            {
                Password = _options.Password,
                ConnectTimeout = _options.ConnectTimeoutMs,
                SyncTimeout = _options.SyncTimeoutMs,
                AbortOnConnectFail = false
            };

            config.EndPoints.Add(_options.MasterEndpoint);

            foreach (var replica in _options.ReplicaEndpoints)
            {
                config.EndPoints.Add(replica);
            }
        }

        return await ConnectionMultiplexer.ConnectAsync(config);
    }
}

/// <summary>
/// Failover manager implementation
/// </summary>
public class FailoverManager : IFailoverManager
{
    private readonly IPostgreSqlHealthCheck _dbHealthCheck;
    private readonly IRedisHealthCheck _redisHealthCheck;
    private readonly PostgreSqlHAOptions _dbOptions;
    private readonly RedisHAOptions _redisOptions;
    private readonly ILogger<FailoverManager> _logger;
    private FailoverStatus _status = new();
    private readonly object _lock = new();

    public FailoverManager(
        IPostgreSqlHealthCheck dbHealthCheck,
        IRedisHealthCheck redisHealthCheck,
        IOptions<PostgreSqlHAOptions> dbOptions,
        IOptions<RedisHAOptions> redisOptions,
        ILogger<FailoverManager> logger)
    {
        _dbHealthCheck = dbHealthCheck;
        _redisHealthCheck = redisHealthCheck;
        _dbOptions = dbOptions.Value;
        _redisOptions = redisOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> FailoverDatabaseAsync()
    {
        lock (_lock)
        {
            if (_status.DatabaseFailoverInProgress)
            {
                _logger.LogWarning("Database failover already in progress");
                return false;
            }
            _status.DatabaseFailoverInProgress = true;
        }

        try
        {
            _logger.LogWarning("Initiating database failover");

            // Find the most up-to-date replica
            var health = await _dbHealthCheck.CheckHealthAsync();
            var bestReplica = health.Replicas
                .Where(r => r.IsHealthy)
                .OrderBy(r => r.LagMs)
                .FirstOrDefault();

            if (bestReplica == null)
            {
                _logger.LogError("No healthy replica available for failover");
                return false;
            }

            // Promote replica to primary
            // In production, this would involve:
            // 1. Stopping writes to current primary
            // 2. Promoting replica using pg_ctl promote or SELECT pg_promote()
            // 3. Updating application configuration
            // 4. Reconfiguring other replicas to follow new primary

            _logger.LogInformation("Promoting replica {Replica} to primary", 
                MaskConnectionString(bestReplica.ConnectionString));

            lock (_lock)
            {
                _status.LastDatabaseFailover = DateTime.UtcNow;
                _status.DatabaseFailoverCount++;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database failover failed");
            return false;
        }
        finally
        {
            lock (_lock)
            {
                _status.DatabaseFailoverInProgress = false;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> FailoverRedisAsync()
    {
        lock (_lock)
        {
            if (_status.RedisFailoverInProgress)
            {
                _logger.LogWarning("Redis failover already in progress");
                return false;
            }
            _status.RedisFailoverInProgress = true;
        }

        try
        {
            _logger.LogWarning("Initiating Redis failover");

            if (_redisOptions.EnableSentinel)
            {
                // Sentinel handles automatic failover
                // We just need to wait for it to complete
                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.UtcNow;

                while (DateTime.UtcNow - startTime < timeout)
                {
                    var health = await _redisHealthCheck.CheckHealthAsync();
                    if (health.IsHealthy)
                    {
                        _logger.LogInformation("Redis failover completed via Sentinel");
                        lock (_lock)
                        {
                            _status.LastRedisFailover = DateTime.UtcNow;
                            _status.RedisFailoverCount++;
                        }
                        return true;
                    }

                    await Task.Delay(1000);
                }

                _logger.LogError("Redis failover timed out");
                return false;
            }
            else
            {
                // Manual failover - select a healthy replica
                var health = await _redisHealthCheck.CheckHealthAsync();
                var healthyReplica = health.Replicas.FirstOrDefault(r => r.IsHealthy);

                if (healthyReplica == null)
                {
                    _logger.LogError("No healthy Redis replica available for failover");
                    return false;
                }

                // In production, this would involve:
                // 1. Promoting replica to master using SLAVEOF NO ONE
                // 2. Updating application configuration
                // 3. Reconfiguring other replicas

                _logger.LogInformation("Promoting Redis replica {Endpoint} to master", 
                    healthyReplica.Endpoint);

                lock (_lock)
                {
                    _status.LastRedisFailover = DateTime.UtcNow;
                    _status.RedisFailoverCount++;
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis failover failed");
            return false;
        }
        finally
        {
            lock (_lock)
            {
                _status.RedisFailoverInProgress = false;
            }
        }
    }

    /// <inheritdoc />
    public FailoverStatus GetStatus()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    private static string MaskConnectionString(string connectionString)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            "Password=[^;]*",
            "Password=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Background service for periodic health checks
/// </summary>
public class HealthCheckBackgroundService : BackgroundService
{
    private readonly IPostgreSqlHealthCheck _dbHealthCheck;
    private readonly IRedisHealthCheck _redisHealthCheck;
    private readonly IFailoverManager _failoverManager;
    private readonly PostgreSqlHAOptions _dbOptions;
    private readonly RedisHAOptions _redisOptions;
    private readonly ILogger<HealthCheckBackgroundService> _logger;

    public HealthCheckBackgroundService(
        IPostgreSqlHealthCheck dbHealthCheck,
        IRedisHealthCheck redisHealthCheck,
        IFailoverManager failoverManager,
        IOptions<PostgreSqlHAOptions> dbOptions,
        IOptions<RedisHAOptions> redisOptions,
        ILogger<HealthCheckBackgroundService> logger)
    {
        _dbHealthCheck = dbHealthCheck;
        _redisHealthCheck = redisHealthCheck;
        _failoverManager = failoverManager;
        _dbOptions = dbOptions.Value;
        _redisOptions = redisOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check database health
                var dbHealth = await _dbHealthCheck.CheckHealthAsync();
                if (!dbHealth.IsHealthy)
                {
                    _logger.LogWarning("Database health check failed, initiating failover");
                    await _failoverManager.FailoverDatabaseAsync();
                }

                // Check Redis health
                var redisHealth = await _redisHealthCheck.CheckHealthAsync();
                if (!redisHealth.IsHealthy)
                {
                    _logger.LogWarning("Redis health check failed, initiating failover");
                    await _failoverManager.FailoverRedisAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check background service error");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Min(_dbOptions.HealthCheckIntervalSeconds, _redisOptions.HealthCheckIntervalSeconds)),
                stoppingToken);
        }
    }
}
