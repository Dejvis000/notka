using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using Notka.Core;

namespace Notka.Mcp
{
    public class NotkaApiClient(HttpClient http)
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly HttpClient _http = http;

        public Task<List<TaskItem>> ListTasksAsync(string filter, CancellationToken cancellationToken) =>
            SendAsync<List<TaskItem>>(HttpMethod.Get, $"/api/tasks?filter={Uri.EscapeDataString(filter)}", null, cancellationToken);

        public Task<List<TaskItem>> SearchTasksAsync(string query, CancellationToken cancellationToken) =>
            SendAsync<List<TaskItem>>(HttpMethod.Get, $"/api/tasks/search?q={Uri.EscapeDataString(query)}", null, cancellationToken);

        public Task<TaskItem> GetTaskAsync(int id, CancellationToken cancellationToken) =>
            SendAsync<TaskItem>(HttpMethod.Get, $"/api/tasks/{id}", null, cancellationToken);

        public Task<TaskItem> CreateTaskAsync(TaskRequest request, CancellationToken cancellationToken) =>
            SendAsync<TaskItem>(HttpMethod.Post, "/api/tasks", request, cancellationToken);

        public Task<TaskItem> UpdateTaskAsync(int id, TaskRequest request, CancellationToken cancellationToken) =>
            SendAsync<TaskItem>(HttpMethod.Put, $"/api/tasks/{id}", request, cancellationToken);

        public Task<TaskItem> CompleteTaskAsync(int id, CancellationToken cancellationToken) =>
            SendAsync<TaskItem>(HttpMethod.Post, $"/api/tasks/{id}/complete", null, cancellationToken);

        public Task<List<StickyNote>> ListNotesAsync(CancellationToken cancellationToken) =>
            SendAsync<List<StickyNote>>(HttpMethod.Get, "/api/notes", null, cancellationToken);

        public Task<StickyNote> CreateNoteAsync(NoteRequest request, CancellationToken cancellationToken) =>
            SendAsync<StickyNote>(HttpMethod.Post, "/api/notes", request, cancellationToken);

        private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body, options: Json);
                }

                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new McpException("Aplikacja Notka nie jest uruchomiona. Uruchom ją i spróbuj ponownie.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new McpException("Aplikacja Notka nie odpowiada. Sprawdź, czy działa, i spróbuj ponownie.");
            }

            using (response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new McpException(@"Notka odrzuciła token dostępu. Uruchom Notkę ponownie; token jest w pliku %LOCALAPPDATA%\Notka\api-token.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new McpException(await DescribeErrorAsync(response, cancellationToken));
                }

                return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
            }
        }

        private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var fallback = $"Aplikacja Notka zwróciła błąd {(int)response.StatusCode}.";
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var messages = errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray()).Select(e => e.GetString());
                    var joined = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
                    if (joined.Length > 0)
                    {
                        return joined;
                    }
                }

                if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
                {
                    return text;
                }

                return root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } name ? name : fallback;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return fallback;
            }
        }
    }
}
