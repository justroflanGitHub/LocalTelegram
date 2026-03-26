using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.SqlClient;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shared.Tracing;

/// <summary>
/// Configuration for distributed tracing with OpenTelemetry and Jaeger
/// </summary>
public static class TracingConfiguration
{
    /// <summary>
    /// Add distributed tracing services
    /// </summary>
    public static IServiceCollection AddDistributedTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion = "1.0.0")
    {
        var tracingConfig = configuration.GetSection("Tracing");
        var isEnabled = tracingConfig.GetValue("Enabled", true);
        var jaegerEndpoint = tracingConfig.GetValue("JaegerEndpoint", "http://jaeger:4317");
        var samplingProbability = tracingConfig.GetValue("SamplingProbability", 1.0);

        if (!isEnabled)
        {
            return services;
        }

        // Configure OpenTelemetry
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new TraceIdRatioBasedSampler(samplingProbability))
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = EnrichWithHttpRequest;
                        options.EnrichWithHttpResponse = EnrichWithHttpResponse;
                        options.Filter = FilterHealthChecks;
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = EnrichWithHttpRequestMessage;
                        options.EnrichWithHttpResponseMessage = EnrichWithHttpResponseMessage;
                    })
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                        options.SetDbStatementForStoredProcedure = true;
                        options.RecordException = true;
                        options.Enrich = EnrichWithSqlActivity;
                    })
                    .AddSource("MassTransit") // For message bus tracing
                    .AddSource("Microsoft.EntityFrameworkCore") // For EF Core tracing
                    .AddJaegerExporter(options =>
                    {
                        options.Endpoint = new Uri(jaegerEndpoint);
                        options.Protocol = JaegerExportProtocol.Grpc;
                        options.ExportProcessorType = ExportProcessorType.Batch;
                        options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                        {
                            MaxQueueSize = 2048,
                            ScheduledDelayMilliseconds = 5000,
                            ExporterTimeoutMilliseconds = 30000,
                            MaxExportBatchSize = 512
                        };
                    });

                // Add Redis instrumentation if StackExchange.Redis is used
                tracing.AddRedisInstrumentation();
            });

        // Add propagation format
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(
            new TextMapPropagator[]
            {
                new TraceContextPropagator(),
                new BaggagePropagator()
            }));

        // Add correlation ID middleware
        services.AddSingleton<CorrelationIdMiddleware>();

        return services;
    }

    /// <summary>
    /// Use distributed tracing middleware
    /// </summary>
    public static IApplicationBuilder UseDistributedTracing(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        return app;
    }

    /// <summary>
    /// Filter health check endpoints from tracing
    /// </summary>
    private static bool FilterHealthChecks(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return !path.Contains("/health") && 
               !path.Contains("/metrics") && 
               !path.Contains("/ready");
    }

    /// <summary>
    /// Enrich span with HTTP request data
    /// </summary>
    private static void EnrichWithHttpRequest(Activity activity, HttpRequest request)
    {
        activity.SetTag("http.request.method", request.Method);
        activity.SetTag("http.request.path", request.Path);
        activity.SetTag("http.request.query", request.QueryString.ToString());
        activity.SetTag("http.request.host", request.Host.ToString());
        activity.SetTag("http.request.scheme", request.Scheme);

        // Add user context if available
        var userId = request.HttpContext.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            activity.SetTag("user.id", userId);
        }

        // Add client IP
        var clientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(clientIp))
        {
            activity.SetTag("http.client_ip", clientIp);
        }

        // Add correlation ID
        if (request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            activity.SetTag("correlation.id", correlationId.ToString());
        }
    }

    /// <summary>
    /// Enrich span with HTTP response data
    /// </summary>
    private static void EnrichWithHttpResponse(Activity activity, HttpResponse response)
    {
        activity.SetTag("http.response.status_code", response.StatusCode);
        
        if (response.StatusCode >= 400)
        {
            activity.SetTag("error", true);
        }
    }

    /// <summary>
    /// Enrich span with HTTP request message data
    /// </summary>
    private static void EnrichWithHttpRequestMessage(Activity activity, HttpRequestMessage request)
    {
        activity.SetTag("http.request.method", request.Method.Method);
        activity.SetTag("http.request.url", request.RequestUri?.ToString());
        activity.SetTag("http.request.host", request.RequestUri?.Host);
    }

    /// <summary>
    /// Enrich span with HTTP response message data
    /// </summary>
    private static void EnrichWithHttpResponseMessage(Activity activity, HttpResponseMessage response)
    {
        activity.SetTag("http.response.status_code", (int)response.StatusCode);
        
        if (!response.IsSuccessStatusCode)
        {
            activity.SetTag("error", true);
        }
    }

    /// <summary>
    /// Enrich span with SQL activity data
    /// </summary>
    private static void EnrichWithSqlActivity(Activity activity, string eventName, object rawObject)
    {
        if (eventName == "OnCustom")
        {
            activity.SetTag("db.system", "postgresql");
        }
    }
}

/// <summary>
/// Middleware for adding correlation IDs to requests
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get or create correlation ID
        string correlationId;
        
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var headerValue))
        {
            correlationId = headerValue.ToString();
        }
        else
        {
            correlationId = Guid.NewGuid().ToString();
        }

        // Store in context items for access in handlers
        context.Items[CorrelationIdHeader] = correlationId;

        // Add to response headers
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Add to current activity if tracing is active
        if (Activity.Current != null)
        {
            Activity.Current.SetTag("correlation.id", correlationId);
        }

        await _next(context);
    }
}

/// <summary>
/// Tracing configuration options
/// </summary>
public class TracingOptions
{
    /// <summary>
    /// Enable or disable tracing
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Jaeger endpoint URL
    /// </summary>
    public string JaegerEndpoint { get; set; } = "http://jaeger:4317";

    /// <summary>
    /// Sampling probability (0.0 to 1.0)
    /// </summary>
    public double SamplingProbability { get; set; } = 1.0;

    /// <summary>
    /// Service name for tracing
    /// </summary>
    public string ServiceName { get; set; } = "unknown-service";

    /// <summary>
    /// Service version
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Paths to exclude from tracing
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics",
        "/ready"
    };
}

/// <summary>
/// Helper class for creating custom spans
/// </summary>
public static class SpanHelper
{
    private static readonly ActivitySource ActivitySource = new("LocalTelegram");

    /// <summary>
    /// Start a new activity span
    /// </summary>
    public static Activity? StartSpan(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind);
    }

    /// <summary>
    /// Start a new activity span with parent context
    /// </summary>
    public static Activity? StartSpan(string name, ActivityContext parentContext, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, parentContext);
    }

    /// <summary>
    /// Add event to current activity
    /// </summary>
    public static void AddEvent(string name, IDictionary<string, object>? tags = null)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        if (tags != null)
        {
            var activityEvent = new ActivityEvent(name, tags: new ActivityTagsCollection(tags));
            activity.AddEvent(activityEvent);
        }
        else
        {
            activity.AddEvent(name);
        }
    }

    /// <summary>
    /// Set error on current activity
    /// </summary>
    public static void SetError(Exception exception)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        activity.SetTag("error", true);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);
        activity.SetTag("error.stacktrace", exception.StackTrace);

        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        }));
    }

    /// <summary>
    /// Get current correlation ID
    /// </summary>
    public static string? GetCorrelationId()
    {
        return Activity.Current?.GetTagItem("correlation.id") as string;
    }

    /// <summary>
    /// Get current trace ID
    /// </summary>
    public static string? GetTraceId()
    {
        return Activity.Current?.TraceId.ToString();
    }

    /// <summary>
    /// Get current span ID
    /// </summary>
    public static string? GetSpanId()
    {
        return Activity.Current?.SpanId.ToString();
    }
}

/// <summary>
/// Extension methods for tracing in services
/// </summary>
public static class TracingExtensions
{
    /// <summary>
    /// Execute a traced operation
    /// </summary>
    public static async Task<T> TraceAsync<T>(
        this object source,
        string operationName,
        Func<Task<T>> operation,
        IDictionary<string, object>? tags = null)
    {
        using var activity = SpanHelper.StartSpan(operationName);
        
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                activity?.SetTag(tag.Key, tag.Value);
            }
        }

        try
        {
            var result = await operation();
            return result;
        }
        catch (Exception ex)
        {
            SpanHelper.SetError(ex);
            throw;
        }
    }

    /// <summary>
    /// Execute a traced operation without return value
    /// </summary>
    public static async Task TraceAsync(
        this object source,
        string operationName,
        Func<Task> operation,
        IDictionary<string, object>? tags = null)
    {
        using var activity = SpanHelper.StartSpan(operationName);
        
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                activity?.SetTag(tag.Key, tag.Value);
            }
        }

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            SpanHelper.SetError(ex);
            throw;
        }
    }
}
