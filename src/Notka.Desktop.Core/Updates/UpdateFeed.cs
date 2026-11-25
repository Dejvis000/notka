using System.Net;
using System.Text.Json;

namespace Notka.Desktop.Updates
{
    public sealed record ReleaseInfo(Version Version, string Tag, Uri PackageUrl, Uri HashUrl, string Notes);

    public interface IUpdateFeed
    {
        Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);
    }

    public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

    public sealed class GitHubUpdateFeed(HttpClient http, Uri url) : IUpdateFeed
    {
        public const string PackageName = "Notka-win-x64.zip";
        public const string HashName = PackageName + ".sha256";
        public const string DefaultUrl = "https://api.github.com/repos/Dejvis000/notka/releases/latest";

        private readonly HttpClient _http = http;
        private readonly Uri _url = url;

        public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(_url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        public static ReleaseInfo? Parse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
                if (AppVersion.Parse(tag) is not { } version || tag is null)
                {
                    return null;
                }

                Uri? package = null;
                Uri? hash = null;
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var link = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
                        {
                            continue;
                        }

                        if (name == PackageName)
                        {
                            package = uri;
                        }
                        else if (name == HashName)
                        {
                            hash = uri;
                        }
                    }
                }

                var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
                return package is null || hash is null ? null : new ReleaseInfo(version, tag, package, hash, notes);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
