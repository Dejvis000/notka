namespace Notka.Api.Middleware
{
    internal sealed class LocalRequestsOnlyMiddleware(RequestDelegate next)
    {
        private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "[::1]",
        };

        private readonly RequestDelegate _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            var fromBrowserPage = context.Request.Headers.ContainsKey("Origin");
            if (fromBrowserPage || !AllowedHosts.Contains(context.Request.Host.Host))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            return _next(context);
        }
    }
}
