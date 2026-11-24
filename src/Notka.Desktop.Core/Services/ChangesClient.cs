using Microsoft.AspNetCore.SignalR.Client;
using Notka.Core;

namespace Notka.Desktop.Services
{
    public interface IChangesClient : IAsyncDisposable
    {
        event Action<ChangeEvent>? TaskChanged;

        event Action<ChangeEvent>? NoteChanged;

        event Action? Reconnected;

        Task StartAsync(CancellationToken cancellationToken = default);
    }

    public sealed class ChangesClient : IChangesClient
    {
        private readonly HubConnection _connection;
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;

        public ChangesClient(Uri baseAddress)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(new Uri(baseAddress, "/hubs/changes"))
                .WithAutomaticReconnect()
                .Build();
            _connection.On<ChangeEvent>("TaskChanged", e => Post(() => TaskChanged?.Invoke(e)));
            _connection.On<ChangeEvent>("NoteChanged", e => Post(() => NoteChanged?.Invoke(e)));
            _connection.Reconnected += _ =>
            {
                Post(() => Reconnected?.Invoke());
                return Task.CompletedTask;
            };
        }

        public event Action<ChangeEvent>? TaskChanged;

        public event Action<ChangeEvent>? NoteChanged;

        public event Action? Reconnected;

        public Task StartAsync(CancellationToken cancellationToken = default) => _connection.StartAsync(cancellationToken);

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        private void Post(Action action)
        {
            if (_context is null)
            {
                action();
            }
            else
            {
                _context.Post(_ => action(), null);
            }
        }
    }
}
