using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Notka.Core;

namespace Notka.Desktop.Services
{
    public class NotkaApiException(string message) : Exception(message);

    public interface INotkaApiClient
    {
        Task<List<TaskItem>> ListTasksAsync(string filter, CancellationToken cancellationToken = default);

        Task<TaskItem> CreateTaskAsync(TaskRequest request, CancellationToken cancellationToken = default);

        Task<TaskItem> CompleteTaskAsync(int id, CancellationToken cancellationToken = default);

        Task<List<StickyNote>> ListNotesAsync(CancellationToken cancellationToken = default);

        Task<StickyNote> CreateNoteAsync(NoteRequest request, CancellationToken cancellationToken = default);

        Task<StickyNote> UpdateNoteAsync(int id, NoteRequest request, CancellationToken cancellationToken = default);

        Task<StickyNote> SetNotePinnedAsync(int id, bool pinned, CancellationToken cancellationToken = default);

        Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default);
    }

    public class NotkaApiClient(HttpClient http) : INotkaApiClient
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly HttpClient _http = http;

        public Task<List<TaskItem>> ListTasksAsync(string filter, CancellationToken cancellationToken = default) =>
            SendAsync<List<TaskItem>>(HttpMethod.Get, $"/api/tasks?filter={Uri.EscapeDataString(filter)}", null, cancellationToken);

        public Task<TaskItem> CreateTaskAsync(TaskRequest request, CancellationToken cancellationToken = default) =>
            SendAsync<TaskItem>(HttpMethod.Post, "/api/tasks", request, cancellationToken);

        public Task<TaskItem> CompleteTaskAsync(int id, CancellationToken cancellationToken = default) =>
            SendAsync<TaskItem>(HttpMethod.Post, $"/api/tasks/{id}/complete", null, cancellationToken);

        public Task<List<StickyNote>> ListNotesAsync(CancellationToken cancellationToken = default) =>
            SendAsync<List<StickyNote>>(HttpMethod.Get, "/api/notes", null, cancellationToken);

        public Task<StickyNote> CreateNoteAsync(NoteRequest request, CancellationToken cancellationToken = default) =>
            SendAsync<StickyNote>(HttpMethod.Post, "/api/notes", request, cancellationToken);

        public Task<StickyNote> UpdateNoteAsync(int id, NoteRequest request, CancellationToken cancellationToken = default) =>
            SendAsync<StickyNote>(HttpMethod.Put, $"/api/notes/{id}", request, cancellationToken);

        public Task<StickyNote> SetNotePinnedAsync(int id, bool pinned, CancellationToken cancellationToken = default) =>
            SendAsync<StickyNote>(HttpMethod.Post, $"/api/notes/{id}/{(pinned ? "pin" : "unpin")}", null, cancellationToken);

        public async Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default) =>
            await SendAsync<object?>(HttpMethod.Delete, $"/api/notes/{id}", null, cancellationToken);

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
                throw new NotkaApiException("Nie można połączyć się z API Notki.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new NotkaApiException("API Notki nie odpowiada.");
            }

            using (response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new NotkaApiException("API Notki odrzuciło token dostępu. Uruchom aplikację ponownie.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new NotkaApiException(await DescribeErrorAsync(response, cancellationToken));
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return default!;
                }

                try
                {
                    return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
                }
                catch (JsonException)
                {
                    throw new NotkaApiException("API Notki zwróciło nieprawidłową odpowiedź.");
                }
            }
        }

        private static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var fallback = $"API Notki zwróciło błąd {(int)response.StatusCode}.";
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
