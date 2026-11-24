using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Notka.Api.Data;
using Notka.Api.Exceptions;
using Notka.Api.Hubs;
using Notka.Core;

namespace Notka.Api.Services
{
    internal interface INoteService
    {
        Task<List<StickyNote>> ListAsync(CancellationToken cancellationToken);

        Task<StickyNote> CreateAsync(NoteRequest request, CancellationToken cancellationToken);

        Task<StickyNote> UpdateAsync(int id, NoteRequest request, CancellationToken cancellationToken);

        Task<StickyNote> SetPinnedAsync(int id, bool pinned, CancellationToken cancellationToken);

        Task DeleteAsync(int id, CancellationToken cancellationToken);
    }

    internal sealed class NoteService(
        IRepository<StickyNote> notes,
        IHubContext<ChangesHub> hub,
        ILogger<NoteService> logger) : INoteService
    {
        private readonly IRepository<StickyNote> _notes = notes;
        private readonly IHubContext<ChangesHub> _hub = hub;
        private readonly ILogger<NoteService> _logger = logger;

        public Task<List<StickyNote>> ListAsync(CancellationToken cancellationToken) =>
            _notes.Query().OrderByDescending(n => n.Pinned).ThenByDescending(n => n.UpdatedAt).ToListAsync(cancellationToken);

        public async Task<StickyNote> CreateAsync(NoteRequest request, CancellationToken cancellationToken)
        {
            var note = new StickyNote { Content = request.Content, Color = request.Color.Trim() };
            _notes.Add(note);
            await _notes.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created note {NoteId}", note.Id);
            await PublishAsync("created", note.Id);
            return note;
        }

        public async Task<StickyNote> UpdateAsync(int id, NoteRequest request, CancellationToken cancellationToken)
        {
            var note = await FindAsync(id, cancellationToken);
            note.Content = request.Content;
            note.Color = request.Color.Trim();
            await _notes.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated note {NoteId}", id);
            await PublishAsync("updated", id);
            return note;
        }

        public async Task<StickyNote> SetPinnedAsync(int id, bool pinned, CancellationToken cancellationToken)
        {
            var note = await FindAsync(id, cancellationToken);
            if (note.Pinned != pinned)
            {
                note.Pinned = pinned;
                await _notes.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Set note {NoteId} pinned to {Pinned}", id, pinned);
                await PublishAsync("updated", id);
            }

            return note;
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken)
        {
            var note = await FindAsync(id, cancellationToken);
            _notes.Remove(note);
            await _notes.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted note {NoteId}", id);
            await PublishAsync("deleted", id);
        }

        private async Task<StickyNote> FindAsync(int id, CancellationToken cancellationToken) =>
            await _notes.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException("notatka", id);

        private Task PublishAsync(string kind, int id) =>
            _hub.Clients.All.SendAsync(ChangesHub.NoteChanged, new ChangeEvent { Kind = kind, Id = id }, CancellationToken.None);
    }
}
