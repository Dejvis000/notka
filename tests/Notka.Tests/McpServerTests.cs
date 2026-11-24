using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Notka.Api.Extensions;
using Notka.Core;
using Notka.Mcp;

namespace Notka.Tests
{
    public sealed class McpServerTests : IAsyncLifetime
    {
        private SqliteConnection _keeper = null!;
        private WebApplication _api = null!;
        private McpClient _client = null!;

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        public async ValueTask InitializeAsync()
        {
            var connection = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keeper = new SqliteConnection(connection);
            _keeper.Open();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["ConnectionStrings:Notka"] = connection;
            builder.Configuration["Notka:Token"] = TestSupport.Token;
            builder.Services.AddNotkaApi(builder.Configuration);
            _api = builder.Build();
            _api.MapNotkaEndpoints();
            await _api.StartAsync(Ct);

            _client = await StartMcpAsync(_api.Urls.First());
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _api.DisposeAsync();
            await _keeper.DisposeAsync();
        }

        private static Task<McpClient> StartMcpAsync(
            string apiUrl,
            string? token = TestSupport.Token,
            string? tokenPath = null,
            Action<string>? onStandardError = null) =>
            McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "notka",
                    Command = "dotnet",
                    Arguments = [McpDllPath()],
                    StandardErrorLines = onStandardError,
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        ["Notka__ApiUrl"] = apiUrl,
                        ["Notka__Token"] = token,
                        ["Notka__TokenPath"] = tokenPath,
                    },
                }),
                cancellationToken: Ct);

        private static string McpDllPath()
        {
            var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var configuration = output.Parent!.Name;
            var root = output;
            while (!File.Exists(Path.Combine(root.FullName, "Notka.sln")))
            {
                root = root.Parent ?? throw new InvalidOperationException("Notka.sln not found above the test output.");
            }

            return Path.Combine(root.FullName, "src", "Notka.Mcp", "bin", configuration, output.Name, "Notka.Mcp.dll");
        }

        private static string Text(CallToolResult result) =>
            string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

        private async Task<T> Call<T>(string tool, Dictionary<string, object?> arguments)
        {
            var result = await _client.CallToolAsync(tool, arguments, cancellationToken: Ct);
            Assert.NotEqual(true, result.IsError);
            return JsonSerializer.Deserialize<T>(Text(result), NotkaApiClient.Json)!;
        }

        [Fact]
        public async Task The_server_exposes_every_tool_with_a_description()
        {
            var tools = await _client.ListToolsAsync(cancellationToken: Ct);

            Assert.Equal(
                ["add_task", "complete_task", "create_note", "list_notes", "list_tasks", "search_tasks", "update_task"],
                tools.Select(t => t.Name).Order());
            Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
        }

        [Fact]
        public async Task A_task_can_be_added_listed_updated_and_completed_through_MCP()
        {
            var tomorrowMorning = DateTime.Now.Date.AddDays(1).AddHours(10);
            var added = await Call<TaskItem>("add_task", new()
            {
                ["title"] = "Oddać poprawki do Panelu Wycen",
                ["dueAt"] = tomorrowMorning.ToString("s"),
                ["priority"] = "high",
                ["tags"] = new[] { "praca" },
            });
            Assert.Equal(Priority.High, added.Priority);
            Assert.Equal(tomorrowMorning, added.DueAt);

            var upcoming = await Call<List<TaskItem>>("list_tasks", new() { ["filter"] = "upcoming" });
            Assert.Equal([added.Id], upcoming.Select(t => t.Id));

            var found = await Call<List<TaskItem>>("search_tasks", new() { ["query"] = "panelu" });
            Assert.Equal([added.Id], found.Select(t => t.Id));

            var updated = await Call<TaskItem>("update_task", new() { ["id"] = added.Id, ["notes"] = "wersja 2" });
            Assert.Equal("wersja 2", updated.Notes);
            Assert.Equal(added.Title, updated.Title);
            Assert.Equal(tomorrowMorning, updated.DueAt);
            Assert.Equal(["praca"], updated.Tags);

            var blankPriority = await Call<TaskItem>("update_task", new() { ["id"] = added.Id, ["priority"] = "" });
            Assert.Equal(Priority.High, blankPriority.Priority);

            var cleared = await Call<TaskItem>("update_task", new() { ["id"] = added.Id, ["clearDueAt"] = true });
            Assert.Null(cleared.DueAt);

            var done = await Call<TaskItem>("complete_task", new() { ["id"] = added.Id });
            Assert.True(done.IsDone);
            Assert.Equal([added.Id], (await Call<List<TaskItem>>("list_tasks", new() { ["filter"] = "Done" })).Select(t => t.Id));
        }

        [Fact]
        public async Task Notes_can_be_created_and_listed_through_MCP()
        {
            var note = await Call<StickyNote>("create_note", new() { ["content"] = "kupić kawę", ["color"] = "blue" });

            var notes = await Call<List<StickyNote>>("list_notes", []);
            Assert.Equal([note.Id], notes.Select(n => n.Id));
            Assert.Equal("blue", notes[0].Color);
        }

        [Theory]
        [InlineData("add_task", "priority", "urgent", "Dozwolone: Low, Normal, High")]
        [InlineData("add_task", "dueAt", "jutro rano", "ISO 8601")]
        [InlineData("list_tasks", "filter", "nope", "Nieznany filtr")]
        public async Task Invalid_arguments_produce_a_readable_error(string tool, string argument, string value, string expected)
        {
            var arguments = new Dictionary<string, object?> { [argument] = value };
            if (tool == "add_task")
            {
                arguments["title"] = "x";
            }

            var result = await _client.CallToolAsync(tool, arguments, cancellationToken: Ct);

            Assert.True(result.IsError);
            Assert.Contains(expected, Text(result));
        }

        [Fact]
        public async Task A_missing_task_is_reported_as_an_error()
        {
            var result = await _client.CallToolAsync("complete_task", new Dictionary<string, object?> { ["id"] = 999 }, cancellationToken: Ct);

            Assert.True(result.IsError);
            Assert.Contains("zadanie (id 999)", Text(result));
        }

        [Fact]
        public async Task A_wrong_token_gives_a_clear_message()
        {
            await using var wrong = await StartMcpAsync(_api.Urls.First(), "wrong-token-0123456789abcdef-0123456789abcdef");

            var result = await wrong.CallToolAsync("list_tasks", new Dictionary<string, object?>(), cancellationToken: Ct);

            Assert.True(result.IsError);
            Assert.Contains("odrzuciła token", Text(result));
        }

        [Fact]
        public async Task The_token_is_read_from_the_token_file_when_no_token_is_configured()
        {
            var path = Path.Combine(Path.GetTempPath(), $"notka-token-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(path, TestSupport.Token + Environment.NewLine, Ct);
            try
            {
                await using var viaFile = await StartMcpAsync(_api.Urls.First(), token: null, tokenPath: path);

                var result = await viaFile.CallToolAsync("list_tasks", new Dictionary<string, object?>(), cancellationToken: Ct);

                Assert.NotEqual(true, result.IsError);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task A_missing_token_file_is_reported_as_the_app_not_running()
        {
            var path = Path.Combine(Path.GetTempPath(), $"notka-missing-{Guid.NewGuid():N}");
            await using var viaFile = await StartMcpAsync(_api.Urls.First(), token: null, tokenPath: path);

            var result = await viaFile.CallToolAsync("list_tasks", new Dictionary<string, object?>(), cancellationToken: Ct);

            Assert.True(result.IsError);
            Assert.Contains("Aplikacja Notka nie jest uruchomiona", Text(result));
        }

        [Theory]
        [InlineData("http://example.com")]
        [InlineData("http://localhost:5123")]
        public async Task The_server_refuses_to_send_the_token_to_anything_but_a_loopback_ip_address(string url)
        {
            var errors = new List<string>();

            await Assert.ThrowsAnyAsync<Exception>(() => StartMcpAsync(url, onStandardError: line =>
            {
                lock (errors)
                {
                    errors.Add(line);
                }
            }));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && !Joined(errors).Contains("loopback IP address"))
            {
                await Task.Delay(50, Ct);
            }

            Assert.Contains("loopback IP address", Joined(errors));
        }

        private static string Joined(List<string> lines)
        {
            lock (lines)
            {
                return string.Join('\n', lines);
            }
        }

        [Fact]
        public async Task A_stopped_app_gives_a_clear_message_instead_of_an_exception()
        {
            await using var offline = await StartMcpAsync("http://127.0.0.1:1");

            var result = await offline.CallToolAsync("list_tasks", new Dictionary<string, object?>(), cancellationToken: Ct);

            Assert.True(result.IsError);
            Assert.Contains("Aplikacja Notka nie jest uruchomiona", Text(result));
        }
    }
}
