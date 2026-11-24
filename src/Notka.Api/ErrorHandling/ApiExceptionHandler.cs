using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Notka.Api.Exceptions;

namespace Notka.Api.ErrorHandling
{
    internal sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
    {
        private readonly IProblemDetailsService _problems = problems;
        private readonly ILogger<ApiExceptionHandler> _logger = logger;

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var (status, title, detail) = exception switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, ApiProblems.NotFoundTitle, exception.Message),
                BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, ApiProblems.BusinessRuleTitle, exception.Message),
                _ => (StatusCodes.Status500InternalServerError, ApiProblems.UnexpectedTitle, ApiProblems.UnexpectedDetail),
            };

            if (status == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            }
            else
            {
                _logger.LogInformation("{Method} {Path} rejected: {Reason}", httpContext.Request.Method, httpContext.Request.Path, exception.Message);
            }

            httpContext.Response.StatusCode = status;
            await _problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
            });
            return true;
        }
    }
}
