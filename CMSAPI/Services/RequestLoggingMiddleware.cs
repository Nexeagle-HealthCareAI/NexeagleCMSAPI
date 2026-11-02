using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CMSAPI.Services
{
    // One log line per request: method, path, status, duration, request id and user id. The request id
    // comes from the caller's X-Request-Id (if well formed) or is generated, and is echoed back on the
    // response and attached to every log line written during the request (logging scope), so a CMS
    // error can be traced to its server log.
    //
    // Privacy: only the path is logged, never the query string (it can carry search text and phone
    // numbers) and never bodies or IP addresses.
    public sealed partial class RequestLoggingMiddleware
    {
        public const string HeaderName = "X-Request-Id";
        private const long SlowRequestMs = 2000;

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
        private static partial Regex ValidRequestId();

        public async Task InvokeAsync(HttpContext context)
        {
            var incoming = context.Request.Headers[HeaderName].ToString();
            var requestId = ValidRequestId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
            context.TraceIdentifier = requestId;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = requestId;
                return Task.CompletedTask;
            });

            var stopwatch = Stopwatch.StartNew();
            using (_logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
            {
                try
                {
                    await _next(context);
                }
                finally
                {
                    stopwatch.Stop();
                    var status = context.Response.StatusCode;
                    var path = context.Request.Path.Value ?? "/";
                    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                 ?? context.User.FindFirst("sub")?.Value
                                 ?? "-";
                    var level = status >= 500 ? LogLevel.Error
                        : stopwatch.ElapsedMilliseconds >= SlowRequestMs || status == StatusCodes.Status429TooManyRequests ? LogLevel.Warning
                        : path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ? LogLevel.Debug
                        : LogLevel.Information;

                    _logger.Log(level, "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms (request {RequestId}, user {UserId})",
                        context.Request.Method, path, status, stopwatch.ElapsedMilliseconds, requestId, userId);
                }
            }
        }
    }
}
