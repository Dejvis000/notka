namespace Notka.Desktop.Updates
{
    public static class AppVersion
    {
        public static readonly Version Dev = new(0, 0, 0);

        public static Version? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var core = text.Trim().TrimStart('v', 'V');
            var cut = core.IndexOfAny(['+', '-']);
            if (cut >= 0)
            {
                core = core[..cut];
            }

            return Version.TryParse(core, out var version)
                ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0))
                : null;
        }

        public static bool IsDev(Version version) => version == Dev;

        public static string Format(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
