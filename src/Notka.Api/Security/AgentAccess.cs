using Microsoft.AspNetCore.Mvc;
using Notka.Core;

namespace Notka.Api.Security
{
    public sealed class AgentAccess
    {
        private volatile bool _allowed = true;

        public bool Allowed
        {
            get => _allowed;
            set => _allowed = value;
        }
    }

    internal sealed class AgentAccessMiddleware(RequestDelegate next, AgentAccess access)
    {
        private readonly RequestDelegate _next = next;
        private readonly AgentAccess _access = access;

        public Task InvokeAsync(HttpContext context)
        {
            var fromAgent = string.Equals(context.Request.Headers[AgentClient.Header], AgentClient.McpValue, StringComparison.OrdinalIgnoreCase);
            if (!fromAgent || _access.Allowed)
            {
                return _next(context);
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Dostęp agentów AI jest wyłączony",
                    Detail = "Dostęp agentów AI jest wyłączony w ustawieniach Notki. Poproś użytkownika o jego włączenie.",
                },
                options: null,
                contentType: "application/problem+json");
        }
    }
}
