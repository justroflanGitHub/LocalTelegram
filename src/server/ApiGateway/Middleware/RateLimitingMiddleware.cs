using System.Net;
using System.Text.Json;

namespace ApiGateway.Middleware;

/// <summary>
/// Rate limiting middleware to protect API endpoints from abuse
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly Dictionary<string, RateLimitCounter> _counters = new();
    private readonly object _lock = new();
    
    // Rate limit configuration
    private const int GeneralRequestsPerMinute = 60;
    private const int AuthRequestsPerMinute = 10;
    private const int UploadRequestsPerMinute = 20;
    private const int MessageRequestsPerMinute = 100;
    
    // Endpoints with specific rate limits
    private static readonly Dictionary<string, int> EndpointLimits = new()
    {
        { "/api/auth/login", AuthRequestsPerMinute },
        { "/api/auth/register", AuthRequestsPerMinute },
        { "/api/auth/refresh", AuthRequestsPerMinute },
        { "/api/files/upload", UploadRequestsPerMinute },
        { "/api/messages", MessageRequestsPerMinute },
    };
    
    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = GetClientIp(context);
        var endpoint = context.Request.Path.Value?.ToLower() ?? "";
        var method = context.Request.Method.ToUpper();
        
        // Get rate limit for this endpoint
        var rateLimit = GetRateLimit(endpoint);
        
        // Create key for rate limiting
        var key = $"{clientIp}:{endpoint}:{method}";
        
        // Check rate limit
        if (!CheckRateLimit(key, rateLimit, out var retryAfter))
        {
            _logger.LogWarning("Rate limit exceeded for {ClientIp} on {Endpoint}", clientIp, endpoint);
            
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Response.Headers["X-RateLimit-Limit"] = rateLimit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";
            
            var response = new
            {
                error = "Too many requests",
                message = $"Rate limit exceeded. Please try again in {retryAfter} seconds.",
                retryAfter
            };
            
            await context.Response.WriteAsJsonAsync(response);
            return;
        }
        
        // Add rate limit headers
        var remaining = GetRemainingRequests(key, rateLimit);
        context.Response.Headers["X-RateLimit-Limit"] = rateLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        
        await _next(context);
    }
    
    private string GetClientIp(HttpContext context)
    {
        // Check for forwarded header (behind reverse proxy)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.TrimEntries);
            if (ips.Length > 0)
            {
                return ips[0];
            }
        }
        
        // Check for real IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }
        
        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
    
    private int GetRateLimit(string endpoint)
    {
        // Check for exact match
        if (EndpointLimits.TryGetValue(endpoint, out var limit))
        {
            return limit;
        }
        
        // Check for prefix match
        foreach (var (prefix, prefixLimit) in EndpointLimits)
        {
            if (endpoint.StartsWith(prefix))
            {
                return prefixLimit;
            }
        }
        
        return GeneralRequestsPerMinute;
    }
    
    private bool CheckRateLimit(string key, int limit, out int retryAfter)
    {
        retryAfter = 0;
        
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddMinutes(-1);
            
            if (!_counters.TryGetValue(key, out var counter))
            {
                _counters[key] = new RateLimitCounter
                {
                    Count = 1,
                    WindowStart = now
                };
                return true;
            }
            
            // Reset counter if window expired
            if (counter.WindowStart < windowStart)
            {
                _counters[key] = new RateLimitCounter
                {
                    Count = 1,
                    WindowStart = now
                };
                return true;
            }
            
            // Check if limit exceeded
            if (counter.Count >= limit)
            {
                retryAfter = (int)(counter.WindowStart.AddMinutes(1) - now).TotalSeconds + 1;
                return false;
            }
            
            // Increment counter
            counter.Count++;
            return true;
        }
    }
    
    private int GetRemainingRequests(string key, int limit)
    {
        lock (_lock)
        {
            if (!_counters.TryGetValue(key, out var counter))
            {
                return limit;
            }
            
            var remaining = limit - counter.Count;
            return Math.Max(0, remaining);
        }
    }
    
    /// <summary>
    /// Clean up expired counters periodically
    /// </summary>
    public void CleanupExpiredCounters()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            var expiredKeys = _counters
                .Where(kvp => kvp.Value.WindowStart < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in expiredKeys)
            {
                _counters.Remove(key);
            }
            
            _logger.LogDebug("Cleaned up {Count} expired rate limit counters", expiredKeys.Count);
        }
    }
}

/// <summary>
/// Rate limit counter for tracking requests
/// </summary>
public class RateLimitCounter
{
    public int Count { get; set; }
    public DateTime WindowStart { get; set; }
}

/// <summary>
/// Background service to clean up expired rate limit counters
/// </summary>
public class RateLimitCleanupService : BackgroundService
{
    private readonly RateLimitingMiddleware _middleware;
    private readonly ILogger<RateLimitCleanupService> _logger;
    
    public RateLimitCleanupService(RateLimitingMiddleware middleware, ILogger<RateLimitCleanupService> logger)
    {
        _middleware = middleware;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                _middleware.CleanupExpiredCounters();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up rate limit counters");
            }
        }
    }
}

/// <summary>
/// Extension methods for rate limiting middleware
/// </summary>
public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }
    
    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        // Rate limiting is handled by middleware, no additional services needed
        return services;
    }
}
