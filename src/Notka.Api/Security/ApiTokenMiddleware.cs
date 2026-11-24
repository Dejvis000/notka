using System.Security.Cryptography;
using System.Text;
using Notka.Api.Hubs;

namespace Notka.Api.Security
{
    internal sealed record ApiTokenHolder(string Token);

    internal sealed class ApiTokenMiddleware(RequestDelegate next, ApiTokenHolder holder)
    {
        private const string BearerPrefix = "Bearer ";

        private readonly RequestDelegate _next = next;
        private readonly byte[] _expected = Encoding.UTF8.GetBytes(holder.Token);

        public Task InvokeAsync(HttpContext context)
        {
            if (IsAuthorized(context.Request))
            {
                return _next(context);
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }

        private bool IsAuthorized(HttpRequest request)
        {
            var supplied = FromHeader(request);
            if (supplied is null && request.Path.StartsWithSegments(ChangesHub.Path))
            {
                supplied = request.Query["access_token"].ToString();
            }

            return !string.IsNullOrEmpty(supplied)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), _expected);
        }

        private static string? FromHeader(HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) ? header[BearerPrefix.Length..].Trim() : null;
        }
    }
}
