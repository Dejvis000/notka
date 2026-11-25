using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Notka.Api.Extensions;
using Notka.Core;

namespace Notka.Tests
{
    public sealed class PerfApiTests : IAsyncLifetime
    {
        private readonly List<string> _lines = [];
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
            builder.Configuration["Notka:Token"] = TestSupport.Token;
            builder.Services.AddNotkaApi(builder.Configuration);
            builder.Services.AddSingleton(new PerfRecorder(_lines.Add));
            _app = builder.Build();
            _app.MapNotkaEndpoints();
            await _app.StartAsync(Ct);
            _http = _app.GetTestClient();
            _http.DefaultRequestHeaders.Authorization = new("Bearer", TestSupport.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.DisposeAsync();
            await _keeper.DisposeAsync();
        }

        private string[] Summary()
        {
            _app.Services.GetRequiredService<PerfRecorder>().Flush(force: true);
            return [.. _lines];
        }

        [Fact]
        public async Task A_request_is_recorded_by_its_route_pattern_without_ids_or_query_text()
        {
            await _http.GetAsync("/api/tasks?filter=Today&search=secret", Ct);
            await _http.GetAsync("/api/notes/7", Ct);
            await _http.GetAsync("/api/notes/8", Ct);

            var lines = Summary();

            Assert.DoesNotContain(lines, l => l.Contains("secret"));
            Assert.Contains(lines, l => l.StartsWith("op=\"api app GET /api/tasks/\" n=1", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("/api/notes/7") || l.Contains("/api/notes/8"));
        }

        [Fact]
        public async Task The_agent_header_is_part_of_the_operation_name()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
            request.Headers.Add("X-Notka-Client", "mcp");
            await _http.SendAsync(request, Ct);

            Assert.Contains(Summary(), l => l.StartsWith("op=\"api mcp GET /api/tasks/\"", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_request_that_ends_in_an_error_is_still_named_by_its_route()
        {
            var response = await _http.DeleteAsync("/api/notes/999", Ct);

            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(Summary(), l => l.StartsWith("op=\"api app DELETE /api/notes/{id:int}\" n=1", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Client_supplied_text_never_reaches_the_operation_names()
        {
            using var request = new HttpRequestMessage(new HttpMethod("SECRET-METHOD"), "/api/tasks");
            request.Headers.Add("X-Notka-Client", "secret\"client");
            await _http.SendAsync(request, Ct);

            var lines = Summary();

            Assert.DoesNotContain(lines, l => l.Contains("secret", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(lines, l => l.StartsWith("op=\"api app other ", StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_live_updates_hub_is_not_recorded()
        {
            await _http.PostAsync("/hubs/changes/negotiate?negotiateVersion=1", null, Ct);

            Assert.DoesNotContain(Summary(), l => l.Contains("hubs"));
        }
    }
}
