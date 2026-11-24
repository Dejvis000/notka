using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using Notka.Core;

namespace Notka.Mcp
{
    internal static class ToolInput
    {
        public static DateTime? ParseDueAt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                throw new McpException($"Nieprawidłowa data '{value}'. Użyj formatu ISO 8601 w czasie lokalnym, np. 2026-11-25T10:00:00.");
            }

            return parsed;
        }

        public static Priority ParsePriority(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Priority.Normal;
            }

            if (!Enum.TryParse<Priority>(value, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new McpException($"Nieprawidłowy priorytet '{value}'. Dozwolone: Low, Normal, High.");
            }

            return parsed;
        }

        public static string ToJson(object value) => JsonSerializer.Serialize(value, NotkaApiClient.Json);
    }
}
