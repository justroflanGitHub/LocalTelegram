using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ApiGateway.Middleware
{
    /// <summary>
    /// Middleware for structured request/response logging with sensitive data masking
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private readonly HashSet<string> _sensitiveFields;
        private readonly HashSet<string> _excludedPaths;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            
            _sensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "password",
                "passwordHash",
                "token",
                "accessToken",
                "refreshToken",
                "secret",
                "secretKey",
                "apiKey",
                "authorization",
                "creditCard",
                "ssn",
                "phoneNumber",
                "email"
            };

            _excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "/health",
                "/healthz",
                "/metrics",
                "/favicon.ico"
            };
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            
            // Skip logging for health checks and metrics
            if (_excludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            
            // Store original response body stream
            var originalBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            // Log request
            var requestLog = await LogRequestAsync(context, requestId);

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Request failed: {Method} {Path}", 
                    requestId, context.Request.Method, path);
                throw;
            }
            finally
            {
                stopwatch.Stop();

                // Log response
                await LogResponseAsync(context, requestId, stopwatch.ElapsedMilliseconds, requestLog);

                // Copy response back to original stream
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalBodyStream);
            }
        }

        private async Task<Dictionary<string, object>> LogRequestAsync(HttpContext context, string requestId)
        {
            var request = context.Request;
            var logData = new Dictionary<string, object>
            {
                ["requestId"] = requestId,
                ["timestamp"] = DateTime.UtcNow.ToString("O"),
                ["method"] = request.Method,
                ["path"] = request.Path.Value ?? "",
                ["queryString"] = MaskQueryString(request.QueryString.Value),
                ["headers"] = MaskHeaders(request.Headers),
                ["ipAddress"] = GetClientIpAddress(context),
                ["userAgent"] = request.Headers["User-Agent"].ToString()
            };

            // Log request body for POST/PUT/PATCH
            if (request.Method != "GET" && request.Method != "DELETE")
            {
                request.EnableBuffering();
                var body = await ReadBodyAsync(request.Body);
                logData["body"] = MaskSensitiveData(body);
                request.Body.Seek(0, SeekOrigin.Begin);
            }

            _logger.LogInformation("[{RequestId}] Request: {Method} {Path} from {IpAddress}",
                requestId, request.Method, request.Path.Value, logData["ipAddress"]);

            // Log full request details at debug level
            _logger.LogDebug("[{RequestId}] Request Details: {RequestData}",
                requestId, JsonSerializer.Serialize(logData));

            return logData;
        }

        private async Task LogResponseAsync(HttpContext context, string requestId, 
            long elapsedMs, Dictionary<string, object> requestLog)
        {
            var response = context.Response;
            response.Body.Seek(0, SeekOrigin.Begin);

            var logData = new Dictionary<string, object>
            {
                ["requestId"] = requestId,
                ["timestamp"] = DateTime.UtcNow.ToString("O"),
                ["statusCode"] = response.StatusCode,
                ["elapsedMs"] = elapsedMs,
                ["contentType"] = response.ContentType,
                ["headers"] = MaskHeaders(response.Headers)
            };

            // Read response body
            var responseBody = await ReadBodyAsync(response.Body);
            response.Body.Seek(0, SeekOrigin.Begin);

            // Truncate large responses
            if (responseBody.Length > 1000)
            {
                logData["body"] = responseBody[..1000] + "... (truncated)";
                logData["bodySize"] = responseBody.Length;
            }
            else
            {
                logData["body"] = MaskSensitiveData(responseBody);
            }

            // Determine log level based on status code
            var logLevel = response.StatusCode switch
            {
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, 
                "[{RequestId}] Response: {StatusCode} in {ElapsedMs}ms",
                requestId, response.StatusCode, elapsedMs);

            // Log full response at debug level
            _logger.LogDebug("[{RequestId}] Response Details: {ResponseData}",
                requestId, JsonSerializer.Serialize(logData));

            // Log performance warning for slow requests
            if (elapsedMs > 1000)
            {
                _logger.LogWarning("[{RequestId}] Slow request: {Method} {Path} took {ElapsedMs}ms",
                    requestId, requestLog["method"], requestLog["path"], elapsedMs);
            }
        }

        private async Task<string> ReadBodyAsync(Stream body)
        {
            try
            {
                body.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(body, leaveOpen: true);
                var content = await reader.ReadToEndAsync();
                body.Seek(0, SeekOrigin.Begin);
                return content;
            }
            catch
            {
                return "[Unable to read body]";
            }
        }

        private string MaskQueryString(string? queryString)
        {
            if (string.IsNullOrEmpty(queryString))
                return "";

            var parts = queryString.TrimStart('?').Split('&');
            var maskedParts = parts.Select(part =>
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length == 2 && IsSensitiveField(keyValue[0]))
                {
                    return $"{keyValue[0]}=***MASKED***";
                }
                return part;
            });

            return "?" + string.Join("&", maskedParts);
        }

        private Dictionary<string, string> MaskHeaders(IHeaderDictionary headers)
        {
            var result = new Dictionary<string, string>();
            foreach (var header in headers)
            {
                if (IsSensitiveField(header.Key))
                {
                    result[header.Key] = "***MASKED***";
                }
                else
                {
                    result[header.Key] = header.Value.ToString();
                }
            }
            return result;
        }

        private string MaskSensitiveData(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            try
            {
                // Try to parse as JSON and mask sensitive fields
                if (content.TrimStart().StartsWith("{") || content.TrimStart().StartsWith("["))
                {
                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(content);
                    var maskedJson = MaskJsonElement(jsonElement);
                    return JsonSerializer.Serialize(maskedJson);
                }
            }
            catch
            {
                // Not valid JSON, return as-is
            }

            return content;
        }

        private object MaskJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        if (IsSensitiveField(property.Name))
                        {
                            dict[property.Name] = "***MASKED***";
                        }
                        else
                        {
                            dict[property.Name] = MaskJsonElement(property.Value);
                        }
                    }
                    return dict;

                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(MaskJsonElement).ToList();

                case JsonValueKind.String:
                    return element.GetString() ?? "";

                case JsonValueKind.Number:
                    return element.TryGetInt64(out var l) ? l : element.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                    return null!;

                default:
                    return element.ToString();
            }
        }

        private bool IsSensitiveField(string fieldName)
        {
            var normalizedField = fieldName.Replace("_", "").Replace("-", "");
            return _sensitiveFields.Contains(normalizedField) ||
                   _sensitiveFields.Any(s => normalizedField.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string GetClientIpAddress(HttpContext context)
        {
            // Check for forwarded headers first
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                // Take the first IP in the chain
                var ip = forwardedFor.Split(',').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(ip))
                    return ip;
            }

            var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIp))
                return realIp;

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
