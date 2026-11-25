namespace Notka.Desktop.Services
{
    public static class StartModes
    {
        public const string Last = "Last";
        public const string Window = "Window";
        public const string Compact = "Compact";
        public const string Tray = "Tray";

        public static string Normalize(string? value) => value is Window or Compact or Tray ? value : Last;
    }

    public sealed record StartModeOption(string Id, string Label);
}
