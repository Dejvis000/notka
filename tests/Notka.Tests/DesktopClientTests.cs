using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Notka.Api.Extensions;
using Notka.Core;
using Notka.Desktop.Services;

namespace Notka.Tests
{
    public sealed class DesktopClientTests : IAsyncLifetime
    {
        private SqliteConnection _keeper = null!;
        private WebApplication _app = null!;
        private NotkaApiClient _client = null!;

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public async ValueTask InitializeAsync()
        {
            var connection = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keeper = new SqliteConnection(connection);
            _keeper.Open();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration["ConnectionStrings:Notka"] = connection;
            builder.Configuration["Notka:Token"] = TestSupport.Token;
            builder.Services.AddNotkaApi(builder.Configuration);
            _app = builder.Build();
            _app.MapNotkaEndpoints();
            await _app.StartAsync(Ct);
            var http = _app.GetTestClient();
            http.DefaultRequestHeaders.Authorization = new("Bearer", TestSupport.Token);
            _client = new NotkaApiClient(http);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            await _keeper.DisposeAsync();
        }

        [Fact]
        public async Task Tasks_round_trip_through_the_real_API()
        {
            var created = await _client.CreateTaskAsync(new TaskRequest { Title = "z klienta", DueAt = DateTime.Today.AddHours(8), Priority = Priority.High }, Ct);
            Assert.Equal(Priority.High, created.Priority);

            Assert.Equal([created.Id], (await _client.ListTasksAsync("Today", Ct)).Select(t => t.Id));

            var done = await _client.CompleteTaskAsync(created.Id, Ct);
            Assert.True(done.IsDone);
            Assert.Equal([created.Id], (await _client.ListTasksAsync("Done", Ct)).Select(t => t.Id));
        }

        [Fact]
        public async Task Notes_round_trip_through_the_real_API()
        {
            var note = await _client.CreateNoteAsync(new NoteRequest { Content = "n", Color = "green" }, Ct);

            var edited = await _client.UpdateNoteAsync(note.Id, new NoteRequest { Content = "zmieniona", Color = "pink" }, Ct);
            Assert.Equal("pink", edited.Color);

            Assert.True((await _client.SetNotePinnedAsync(note.Id, true, Ct)).Pinned);
            Assert.Equal([note.Id], (await _client.ListNotesAsync(Ct)).Select(n => n.Id));

            await _client.DeleteNoteAsync(note.Id, Ct);
            Assert.Empty(await _client.ListNotesAsync(Ct));
        }

        [Fact]
        public async Task A_validation_error_from_the_API_becomes_a_readable_exception()
        {
            var exception = await Assert.ThrowsAsync<NotkaApiException>(() => _client.CreateTaskAsync(new TaskRequest { Title = " " }, Ct));

            Assert.Equal("Tytuł jest wymagany.", exception.Message);
        }

        [Fact]
        public async Task A_missing_note_is_reported_with_the_API_message()
        {
            var exception = await Assert.ThrowsAsync<NotkaApiException>(() => _client.DeleteNoteAsync(999, Ct));

            Assert.Contains("notatka (id 999)", exception.Message);
        }

        [Fact]
        public async Task A_rejected_token_becomes_a_readable_exception()
        {
            var exception = await Assert.ThrowsAsync<NotkaApiException>(() => new NotkaApiClient(_app.GetTestClient()).ListNotesAsync(Ct));

            Assert.Contains("odrzuciło token", exception.Message);
        }

        [Fact]
        public async Task An_invalid_response_body_becomes_a_readable_exception()
        {
            using var http = new HttpClient(new StubHandler("not json")) { BaseAddress = new Uri("http://localhost") };

            var exception = await Assert.ThrowsAsync<NotkaApiException>(() => new NotkaApiClient(http).ListNotesAsync(Ct));

            Assert.Equal("API Notki zwróciło nieprawidłową odpowiedź.", exception.Message);
        }

        [Fact]
        public async Task An_unreachable_API_gives_a_clear_message()
        {
            using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };

            var exception = await Assert.ThrowsAsync<NotkaApiException>(() => new NotkaApiClient(http).ListNotesAsync(Ct));

            Assert.Equal("Nie można połączyć się z API Notki.", exception.Message);
        }

        private sealed class StubHandler(string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
