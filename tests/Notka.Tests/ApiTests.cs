using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Notka.Api.Extensions;
using Notka.Api.Hubs;
using Notka.Core;

namespace Notka.Tests
{
    public sealed class ApiTests : IAsyncLifetime
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        private SqliteConnection _keeper = null!;
        private WebApplication _app = null!;
        private HttpClient _http = null!;

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public async ValueTask InitializeAsync()
        {
            var connection = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keeper = new SqliteConnection(connection);
            _keeper.Open();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration["ConnectionStrings:Notka"] = connection;
            builder.Services.AddNotkaApi(builder.Configuration);
            _app = builder.Build();
            _app.MapNotkaEndpoints();
            await _app.StartAsync(Ct);
            _http = _app.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync();
            await _keeper.DisposeAsync();
        }

        private Task<HttpResponseMessage> Get(string url) => _http.GetAsync(url, Ct);

        private Task<HttpResponseMessage> Delete(string url) => _http.DeleteAsync(url, Ct);

        private Task<HttpResponseMessage> PostEmpty(string url) => _http.PostAsync(url, null, Ct);

        private Task<HttpResponseMessage> PostJson(string url, object body) => _http.PostAsJsonAsync(url, body, Json, Ct);

        private Task<HttpResponseMessage> PutJson(string url, object body) => _http.PutAsJsonAsync(url, body, Json, Ct);

        private static async Task<T> Read<T>(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

        private async Task<T> GetJson<T>(string url) => await Read<T>(await Get(url));

        private async Task<TaskItem> CreateTask(TaskRequest request)
        {
            var response = await PostJson("/api/tasks", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await Read<TaskItem>(response);
        }

        private async Task<List<string>> Titles(string url) =>
            (await GetJson<List<TaskItem>>(url)).Select(t => t.Title).ToList();

        [Fact]
        public async Task A_task_can_be_created_updated_completed_and_deleted()
        {
            var created = await CreateTask(new TaskRequest { Title = "  Kup mleko ", Notes = "2 litry", Tags = ["dom", " Dom ", ""] });
            Assert.Equal("Kup mleko", created.Title);
            Assert.Equal(["dom"], created.Tags);
            Assert.False(created.IsDone);

            var updated = await Read<TaskItem>(await PutJson($"/api/tasks/{created.Id}", new TaskRequest { Title = "Kup chleb", Priority = Priority.High }));
            Assert.Equal("Kup chleb", updated.Title);
            Assert.Equal(Priority.High, updated.Priority);

            var done = await Read<TaskItem>(await PostEmpty($"/api/tasks/{created.Id}/complete"));
            Assert.True(done.IsDone);
            Assert.NotNull(done.CompletedAt);

            Assert.Equal(HttpStatusCode.NoContent, (await Delete($"/api/tasks/{created.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Get($"/api/tasks/{created.Id}")).StatusCode);
        }

        [Theory]
        [InlineData("", "Normal")]
        [InlineData("   ", "Normal")]
        [InlineData("ok", "Urgent")]
        [InlineData("ok", "7")]
        public async Task A_task_with_invalid_input_is_rejected(string title, string priority)
        {
            var response = await _http.PostAsync("/api/tasks", JsonContent.Create(new { title, priority }), Ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task A_task_without_title_is_rejected_with_a_problem_response()
        {
            var response = await _http.PostAsync("/api/tasks", JsonContent.Create(new { notes = "x" }), Ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Tytuł jest wymagany.", await response.Content.ReadAsStringAsync(Ct));
        }

        [Fact]
        public async Task A_too_long_tag_is_a_business_rule_violation()
        {
            var response = await PostJson("/api/tasks", new TaskRequest { Title = "t", Tags = [new string('x', TaskItem.TagMaxLength + 1)] });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Empty(await Titles("/api/tasks"));
        }

        [Theory]
        [InlineData("Host", "evil.example")]
        [InlineData("Origin", "https://evil.example")]
        public async Task Requests_from_foreign_hosts_or_browser_origins_are_forbidden(string header, string value)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
            if (header == "Host")
            {
                request.Headers.Host = value;
            }
            else
            {
                request.Headers.Add(header, value);
            }

            var response = await _http.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData("localhost")]
        [InlineData("127.0.0.1:5123")]
        [InlineData("[::1]:5123")]
        public async Task Requests_to_loopback_hosts_are_allowed(string host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
            request.Headers.Host = host;

            var response = await _http.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task A_missing_task_stays_404_for_clients_that_do_not_accept_JSON()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks/999");
            request.Headers.Accept.ParseAdd("text/plain");

            var response = await _http.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Too_long_fields_are_rejected_in_Polish()
        {
            var response = await PostJson("/api/tasks", new TaskRequest { Title = new string('a', TaskItem.TitleMaxLength + 1) });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("maksymalnie 500 znaków", await response.Content.ReadAsStringAsync(Ct));
        }

        [Fact]
        public async Task Filters_split_tasks_by_due_date()
        {
            var today = DateTime.Now.Date;
            await CreateTask(new TaskRequest { Title = "wczoraj", DueAt = today.AddDays(-1).AddHours(12) });
            await CreateTask(new TaskRequest { Title = "dzis", DueAt = today.AddHours(12) });
            await CreateTask(new TaskRequest { Title = "jutro", DueAt = today.AddDays(1).AddHours(12) });
            await CreateTask(new TaskRequest { Title = "bez terminu" });
            var finished = await CreateTask(new TaskRequest { Title = "zrobione", DueAt = today.AddHours(12) });
            await PostEmpty($"/api/tasks/{finished.Id}/complete");

            Assert.Equal(["dzis"], await Titles("/api/tasks?filter=Today"));
            Assert.Equal(["wczoraj"], await Titles("/api/tasks?filter=Overdue"));
            Assert.Equal(["jutro"], await Titles("/api/tasks?filter=Upcoming"));
            Assert.Equal(["zrobione"], await Titles("/api/tasks?filter=Done"));
            Assert.Equal(5, (await Titles("/api/tasks")).Count);
        }

        [Fact]
        public async Task The_filter_ignores_case_and_unknown_values_are_rejected()
        {
            await CreateTask(new TaskRequest { Title = "a", DueAt = DateTime.Now.Date.AddHours(12) });

            Assert.Equal(["a"], await Titles("/api/tasks?filter=today"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Get("/api/tasks?filter=7")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Get("/api/tasks?filter=nope")).StatusCode);
        }

        [Fact]
        public async Task Search_matches_title_and_notes_ignoring_ASCII_case()
        {
            await CreateTask(new TaskRequest { Title = "Zadzwoń do Anny" });
            await CreateTask(new TaskRequest { Title = "Raport", Notes = "Wyślij do ANNY" });
            await CreateTask(new TaskRequest { Title = "Inne" });

            Assert.Equal(["Raport", "Zadzwoń do Anny"], (await Titles("/api/tasks/search?q=anny")).Order().ToList());
        }

        [Fact]
        public async Task Search_treats_wildcards_literally()
        {
            await CreateTask(new TaskRequest { Title = "100% pewne" });
            await CreateTask(new TaskRequest { Title = "zwykly" });

            Assert.Equal(["100% pewne"], await Titles("/api/tasks/search?q=%25"));
            Assert.Empty(await Titles("/api/tasks/search?q=_"));
        }

        [Fact]
        public async Task An_empty_search_query_is_a_business_rule_violation()
        {
            var response = await Get("/api/tasks/search?q=");

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task A_missing_task_is_reported_in_Polish()
        {
            var response = await Get("/api/tasks/999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.Contains("Nie znaleziono zasobu.", body);
            Assert.Contains("zadanie (id 999)", body);
        }

        [Fact]
        public async Task Null_tags_are_ignored_and_a_UTC_due_date_is_stored_as_local_time()
        {
            var utc = new DateTime(2030, 6, 1, 23, 30, 0, DateTimeKind.Utc);
            var response = await _http.PostAsync("/api/tasks", JsonContent.Create(new { title = "t", tags = new string?[] { null, "x" }, dueAt = utc }), Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await Read<TaskItem>(response);
            var fetched = await GetJson<TaskItem>($"/api/tasks/{created.Id}");
            Assert.Equal(["x"], fetched.Tags);
            Assert.Equal(utc.ToLocalTime(), fetched.DueAt);
        }

        [Fact]
        public async Task Notes_can_be_created_pinned_edited_and_deleted()
        {
            var first = await Read<StickyNote>(await PostJson("/api/notes", new NoteRequest { Content = "pierwsza" }));
            var second = await Read<StickyNote>(await PostJson("/api/notes", new NoteRequest { Content = "druga", Color = "blue" }));
            Assert.Equal("blue", second.Color);
            Assert.NotEqual(default, first.CreatedAt);

            await PostEmpty($"/api/notes/{first.Id}/pin");
            var list = await GetJson<List<StickyNote>>("/api/notes");
            Assert.Equal([first.Id, second.Id], list.Select(n => n.Id));
            Assert.True(list[0].Pinned);

            await PostEmpty($"/api/notes/{first.Id}/unpin");
            Assert.All(await GetJson<List<StickyNote>>("/api/notes"), n => Assert.False(n.Pinned));

            var edited = await Read<StickyNote>(await PutJson($"/api/notes/{second.Id}", new NoteRequest { Content = "zmieniona" }));
            Assert.Equal("zmieniona", edited.Content);
            Assert.Equal(second.CreatedAt, edited.CreatedAt);
            Assert.True(edited.UpdatedAt > second.UpdatedAt);

            Assert.Equal(HttpStatusCode.NoContent, (await Delete($"/api/notes/{first.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Delete($"/api/notes/{first.Id}")).StatusCode);
        }

        [Fact]
        public async Task A_note_without_color_or_content_is_rejected()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PostJson("/api/notes", new NoteRequest { Content = "x", Color = " " })).StatusCode);
            var missingContent = await _http.PostAsync("/api/notes", JsonContent.Create(new { color = "red" }), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingContent.StatusCode);
            Assert.Contains("Treść jest wymagana.", await missingContent.Content.ReadAsStringAsync(Ct));
        }

        [Fact]
        public async Task The_hub_publishes_task_and_note_changes()
        {
            var server = _app.GetTestServer();
            await using var hub = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, ChangesHub.Path), o =>
                {
                    o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    o.Transports = HttpTransportType.LongPolling;
                })
                .AddJsonProtocol(o => o.PayloadSerializerOptions = Json)
                .Build();
            var taskEvents = new List<ChangeEvent>();
            var noteEvents = new List<ChangeEvent>();
            var received = new SemaphoreSlim(0);
            hub.On<ChangeEvent>(ChangesHub.TaskChanged, e => { lock (taskEvents) { taskEvents.Add(e); } received.Release(); });
            hub.On<ChangeEvent>(ChangesHub.NoteChanged, e => { lock (noteEvents) { noteEvents.Add(e); } received.Release(); });
            await hub.StartAsync(Ct);

            var task = await CreateTask(new TaskRequest { Title = "sygnal" });
            await PostEmpty($"/api/tasks/{task.Id}/complete");
            await Delete($"/api/tasks/{task.Id}");
            var note = await Read<StickyNote>(await PostJson("/api/notes", new NoteRequest { Content = "n" }));
            await Delete($"/api/notes/{note.Id}");

            for (var i = 0; i < 5; i++)
            {
                Assert.True(await received.WaitAsync(TimeSpan.FromSeconds(5), Ct));
            }

            Assert.Equal(["created", "updated", "deleted"], taskEvents.Select(e => e.Kind));
            Assert.All(taskEvents, e => Assert.Equal(task.Id, e.Id));
            Assert.Equal(["created", "deleted"], noteEvents.Select(e => e.Kind));
        }
    }
}
