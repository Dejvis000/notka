using System.Diagnostics;
using Notka.Api.Hubs;
using Notka.Core;

namespace Notka.Api.Middleware
{
    internal sealed class PerfMiddleware(RequestDelegate next, PerfRecorder recorder)
    {
        private static readonly HashSet<string> Methods = new(StringComparer.Ordinal) { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

        private readonly RequestDelegate _next = next;
        private readonly PerfRecorder _recorder = recorder;

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(ChangesHub.Path) || context.WebSockets.IsWebSocketRequest)
            {
                await _next(context);
                return;
            }

            var client = context.Request.Headers[AgentClient.Header].ToString() == AgentClient.McpValue ? "mcp" : "app";
            var method = Methods.Contains(context.Request.Method) ? context.Request.Method : "other";
            var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var start = Stopwatch.GetTimestamp();
            try
            {
                await _next(context);
            }
            finally
            {
                _recorder.Record($"api {client} {method} {pattern}", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        }
    }
}
