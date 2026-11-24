using System.ComponentModel.DataAnnotations;
using Notka.Core;

namespace Notka.Api.Endpoints
{
    internal sealed class ValidationFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            foreach (var argument in context.Arguments)
            {
                if (argument is not (TaskRequest or NoteRequest))
                {
                    continue;
                }

                var results = new List<ValidationResult>();
                if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
                {
                    var errors = results
                        .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
                        .ToDictionary(g => g.Key, g => g.Select(r => r.ErrorMessage ?? string.Empty).ToArray());
                    return TypedResults.ValidationProblem(errors);
                }
            }

            return await next(context);
        }
    }
}
