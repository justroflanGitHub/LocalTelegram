using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Json;
using Serilog.Sinks.Grafana.Loki;

namespace Shared.Logging;

/// <summary>
/// Configuration for centralized logging with Serilog and Loki
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Configure Serilog with centralized logging
    /// </summary>
    public static Logger CreateLogger(
        IConfiguration configuration,
        string serviceName,
        string serviceVersion = "1.0.0")
    {
        var loggingConfig = configuration.GetSection("Logging");
        var lokiEnabled = loggingConfig.GetValue("LokiEnabled", true);
        var lokiUrl = loggingConfig.GetValue("LokiUrl", "http://loki:3100");
        var logLevel = loggingConfig.GetValue("LogLevel", "Information");
        var consoleEnabled = loggingConfig.GetValue("ConsoleEnabled", true);
        var fileEnabled = loggingConfig.GetValue("FileEnabled", false);
        var filePath = loggingConfig.GetValue("FilePath", "logs/log-.txt");
        var jsonFormat = loggingConfig.GetValue("JsonFormat", true);

        var level = Enum.Parse<LogEventLevel>(logLevel, true);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("ServiceVersion", serviceVersion)
            .Enrich.WithProperty("Environment", configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production")
            .Enrich.WithCorrelationId()
            .Enrich.WithCorrelationIdHeader("X-Correlation-ID")
            .Filter.ByExcluding(Matching.FromSource("Microsoft.AspNetCore.Routing"))
            .Filter.ByExcluding(Matching.FromSource("Microsoft.AspNetCore.StaticFiles"))
            .Filter.ByExcluding(logEvent => 
                logEvent.MessageTemplate.Text.Contains("/health") ||
                logEvent.MessageTemplate.Text.Contains("/metrics"));

        // Add console sink
        if (consoleEnabled)
        {
            if (jsonFormat)
            {
                loggerConfig.WriteTo.Console(new CompactJsonFormatter());
            }
            else
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
            }
        }

        // Add file sink with rotation
        if (fileEnabled)
        {
            loggerConfig.WriteTo.File(
                path: filePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 100_000_000, // 100MB
                rollOnFileSizeLimit: true,
                formatter: jsonFormat ? new CompactJsonFormatter() : null,
                outputTemplate: jsonFormat ? null : 
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }

        // Add Loki sink for centralized logging
        if (lokiEnabled)
        {
            loggerConfig.WriteTo.GrafanaLoki(
                uri: lokiUrl,
                labels: new[]
                {
                    new LokiLabel { Key = "service", Value = serviceName },
                    new LokiLabel { Key = "version", Value = serviceVersion }
                },
                propertiesAsLabels: new[] { "Level", "ServiceName" },
                logEventToLokiMessageMapper: LogEventToLokiMessageMapper);
        }

        return loggerConfig.CreateLogger();
    }

    /// <summary>
    /// Map log event to Loki message format
    /// </summary>
    private static string LogEventToLokiMessageMapper(LogEvent logEvent)
    {
        // Include all properties in the message
        var message = logEvent.RenderMessage();
        
        if (logEvent.Exception != null)
        {
            message += $" | Exception: {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}";
        }

        return message;
    }

    /// <summary>
    /// Add centralized logging services
    /// </summary>
    public static IServiceCollection AddCentralizedLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add log context accessor for enriching logs with custom properties
        services.AddSingleton<ILogContextEnricher, LogContextEnricher>();

        return services;
    }

    /// <summary>
    /// Use centralized logging middleware
    /// </summary>
    public static IApplicationBuilder UseCentralizedLogging(this IApplicationBuilder app)
    {
        // Add correlation ID to all requests
        app.Use(async (context, next) =>
        {
            var correlationId = context.TraceIdentifier;
            
            if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var headerValue))
            {
                correlationId = headerValue.ToString();
            }
            else
            {
                context.Request.Headers["X-Correlation-ID"] = correlationId;
            }

            context.Response.Headers["X-Correlation-ID"] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next();
            }
        });

        return app;
    }
}

/// <summary>
/// Custom log context enricher
/// </summary>
public class LogContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Add thread ID
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ThreadId", Environment.CurrentManagedThreadId));

        // Add timestamp in ISO 8601 format
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "TimestampIso", logEvent.Timestamp.ToString("o")));
    }
}

/// <summary>
/// Sensitive data masking enricher
/// </summary>
public class SensitiveDataMaskingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwd",
        "pwd",
        "secret",
        "secretkey",
        "secret_key",
        "apikey",
        "api_key",
        "token",
        "accesstoken",
        "access_token",
        "refreshtoken",
        "refresh_token",
        "authorization",
        "credential",
        "credentials",
        "privatekey",
        "private_key",
        "sessionid",
        "session_id",
        "cookie",
        "ssn",
        "socialsecurity",
        "creditcard",
        "credit_card",
        "cardnumber",
        "card_number",
        "cvv",
        "pin",
        "otp",
        "totp",
        "recoverycode",
        "recovery_code"
    };

    private const string MaskValue = "***MASKED***";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            if (IsSensitiveField(property.Key))
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(property.Key, MaskValue));
            }
            else if (property.Value is ScalarValue scalarValue && scalarValue.Value is string stringValue)
            {
                var maskedValue = MaskSensitivePatterns(stringValue);
                if (maskedValue != stringValue)
                {
                    logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(property.Key, maskedValue));
                }
            }
        }
    }

    private static bool IsSensitiveField(string fieldName)
    {
        return SensitiveFields.Contains(fieldName) ||
               SensitiveFields.Any(s => fieldName.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private static string MaskSensitivePatterns(string value)
    {
        // Mask potential tokens (long alphanumeric strings)
        if (value.Length > 32 && IsAlphanumeric(value))
        {
            return value.Substring(0, 8) + "***MASKED***";
        }

        // Mask email-like patterns in non-email fields
        // (Email masking can be enabled separately if needed)

        // Mask potential credit card numbers (16 digits)
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"\b\d{13,16}\b"))
        {
            return "***MASKED***";
        }

        return value;
    }

    private static bool IsAlphanumeric(string value)
    {
        return value.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }
}

/// <summary>
/// Logging configuration options
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// Enable Loki centralized logging
    /// </summary>
    public bool LokiEnabled { get; set; } = true;

    /// <summary>
    /// Loki server URL
    /// </summary>
    public string LokiUrl { get; set; } = "http://loki:3100";

    /// <summary>
    /// Minimum log level
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// Enable console logging
    /// </summary>
    public bool ConsoleEnabled { get; set; } = true;

    /// <summary>
    /// Enable file logging
    /// </summary>
    public bool FileEnabled { get; set; } = false;

    /// <summary>
    /// Log file path pattern
    /// </summary>
    public string FilePath { get; set; } = "logs/log-.txt";

    /// <summary>
    /// Use JSON format for logs
    /// </summary>
    public bool JsonFormat { get; set; } = true;

    /// <summary>
    /// Maximum number of retained log files
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>
    /// Maximum log file size in bytes
    /// </summary>
    public long FileSizeLimitBytes { get; set; } = 100_000_000; // 100MB

    /// <summary>
    /// Enable sensitive data masking
    /// </summary>
    public bool MaskSensitiveData { get; set; } = true;
}

/// <summary>
/// Helper class for structured logging
/// </summary>
public static class LogHelper
{
    /// <summary>
    /// Log with structured properties
    /// </summary>
    public static IDisposable BeginScope(string operationName, params (string Key, object Value)[] properties)
    {
        var disposables = new List<IDisposable>();

        foreach (var (key, value) in properties)
        {
            disposables.Add(LogContext.PushProperty(key, value));
        }

        disposables.Add(LogContext.PushProperty("Operation", operationName));

        return new CompositeDisposable(disposables);
    }

    /// <summary>
    /// Log user action
    /// </summary>
    public static void LogUserAction(this ILogger logger, string action, Guid userId, params (string Key, object Value)[] details)
    {
        using var scope = BeginScope(action, details);
        logger.LogInformation("User {UserId} performed action: {Action}", userId, action);
    }

    /// <summary>
    /// Log security event
    /// </summary>
    public static void LogSecurityEvent(this ILogger logger, string eventType, Guid? userId, string description, params (string Key, object Value)[] details)
    {
        using var scope = BeginScope("Security", details);
        logger.LogWarning("Security event: {EventType} - {Description}. User: {UserId}", 
            eventType, description, userId?.ToString() ?? "Anonymous");
    }

    /// <summary>
    /// Log performance metric
    /// </summary>
    public static void LogPerformance(this ILogger logger, string operation, long durationMs, params (string Key, object Value)[] details)
    {
        using var scope = BeginScope("Performance", details);
        
        if (durationMs > 1000)
        {
            logger.LogWarning("Slow operation: {Operation} took {Duration}ms", operation, durationMs);
        }
        else
        {
            logger.LogDebug("Operation: {Operation} completed in {Duration}ms", operation, durationMs);
        }
    }

    /// <summary>
    /// Log API request
    /// </summary>
    public static void LogApiRequest(
        this ILogger logger,
        string method,
        string path,
        int statusCode,
        long durationMs,
        Guid? userId = null,
        string? correlationId = null)
    {
        using var scope = BeginScope("ApiRequest",
            ("Method", method),
            ("Path", path),
            ("StatusCode", statusCode),
            ("DurationMs", durationMs),
            ("UserId", userId?.ToString() ?? "Anonymous"),
            ("CorrelationId", correlationId ?? "N/A"));

        if (statusCode >= 500)
        {
            logger.LogError("API {Method} {Path} returned {StatusCode} in {Duration}ms",
                method, path, statusCode, durationMs);
        }
        else if (statusCode >= 400)
        {
            logger.LogWarning("API {Method} {Path} returned {StatusCode} in {Duration}ms",
                method, path, statusCode, durationMs);
        }
        else
        {
            logger.LogInformation("API {Method} {Path} returned {StatusCode} in {Duration}ms",
                method, path, statusCode, durationMs);
        }
    }
}

/// <summary>
/// Composite disposable for managing multiple disposables
/// </summary>
internal class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _disposables;
    private bool _disposed;

    public CompositeDisposable(List<IDisposable> disposables)
    {
        _disposables = disposables;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _disposed = true;
    }
}
