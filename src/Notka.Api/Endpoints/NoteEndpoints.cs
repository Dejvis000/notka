using Notka.Api.Services;
using Notka.Core;

namespace Notka.Api.Endpoints
{
    internal static class NoteEndpoints
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/notes").AddEndpointFilter<ValidationFilter>();

            group.MapGet("/", async (INoteService notes, CancellationToken ct) =>
                TypedResults.Ok(await notes.ListAsync(ct)));

            group.MapPost("/", async (NoteRequest request, INoteService notes, CancellationToken ct) =>
            {
                var note = await notes.CreateAsync(request, ct);
                return TypedResults.Created($"/api/notes/{note.Id}", note);
            });

            group.MapPut("/{id:int}", async (int id, NoteRequest request, INoteService notes, CancellationToken ct) =>
                TypedResults.Ok(await notes.UpdateAsync(id, request, ct)));

            group.MapPost("/{id:int}/pin", async (int id, INoteService notes, CancellationToken ct) =>
                TypedResults.Ok(await notes.SetPinnedAsync(id, true, ct)));

            group.MapPost("/{id:int}/unpin", async (int id, INoteService notes, CancellationToken ct) =>
                TypedResults.Ok(await notes.SetPinnedAsync(id, false, ct)));

            group.MapDelete("/{id:int}", async (int id, INoteService notes, CancellationToken ct) =>
            {
                await notes.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            });
        }
    }
}
