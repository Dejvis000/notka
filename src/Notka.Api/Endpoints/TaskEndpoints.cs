using Notka.Api.Services;
using Notka.Core;

namespace Notka.Api.Endpoints
{
    internal static class TaskEndpoints
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/tasks").AddEndpointFilter<ValidationFilter>();

            group.MapGet("/", async (string? filter, ITaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.ListAsync(filter, ct)));

            group.MapGet("/search", async (string q, ITaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.SearchAsync(q, ct)));

            group.MapGet("/{id:int}", async (int id, ITaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.GetAsync(id, ct)));

            group.MapPost("/", async (TaskRequest request, ITaskService tasks, CancellationToken ct) =>
            {
                var task = await tasks.CreateAsync(request, ct);
                return TypedResults.Created($"/api/tasks/{task.Id}", task);
            });

            group.MapPut("/{id:int}", async (int id, TaskRequest request, ITaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.UpdateAsync(id, request, ct)));

            group.MapPost("/{id:int}/complete", async (int id, ITaskService tasks, CancellationToken ct) =>
                TypedResults.Ok(await tasks.CompleteAsync(id, ct)));

            group.MapDelete("/{id:int}", async (int id, ITaskService tasks, CancellationToken ct) =>
            {
                await tasks.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            });
        }
    }
}
