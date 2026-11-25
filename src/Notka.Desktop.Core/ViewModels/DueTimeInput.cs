using System.Text.RegularExpressions;

namespace Notka.Desktop.ViewModels
{
    public static partial class DueTimeInput
    {
        public static bool TryParse(string? text, out TimeSpan time)
        {
            time = default;
            if (text is null || Pattern().Match(text) is not { Success: true } match)
            {
                return false;
            }

            var hour = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            if (hour > 23 || minute > 59)
            {
                return false;
            }

            time = new TimeSpan(hour, minute, 0);
            return true;
        }

        [GeneratedRegex(@"^\s*([0-9]{1,2})(?:[:.]?([0-9]{2}))?\s*$")]
        private static partial Regex Pattern();
    }
}
