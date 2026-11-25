using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Notka.Desktop.Updates
{
    public static partial class UpdateHash
    {
        public static string? ParseExpected(string text)
        {
            var match = Sha256Pattern().Match(text);
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }

        public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        [GeneratedRegex("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])")]
        private static partial Regex Sha256Pattern();
    }

    public sealed record UpdateResult(bool Ok, string Version, string? Message, bool Attempted = true, bool Restored = false)
    {
        public static UpdateResult? TryParse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var ok = Flag(root, "ok", false);
                var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? string.Empty : string.Empty;
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                return new UpdateResult(ok, version, message, Flag(root, "attempted", true), Flag(root, "restored", false));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        private static bool Flag(JsonElement root, string name, bool fallback) =>
            root.TryGetProperty(name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.ValueKind == JsonValueKind.True
                : fallback;
    }
}
