using System.Text.Json;
using Notka.Desktop.Taskbar;

namespace Notka.Desktop.Services
{
    public sealed class AppSettings(string path)
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly string _path = path;

        public static string DefaultPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "settings.json");

        public bool ShowStrip { get; set; } = true;

        public string StripTarget { get; set; } = StripTargets.Primary;

        public bool StripBackground { get; set; } = true;

        public bool AllowAgents { get; set; } = true;

        public bool Compact { get; set; }

        public bool AlwaysOnTop { get; set; }

        public bool CompactShowsTasks { get; set; }

        public int NormalWidth { get; set; }

        public int NormalHeight { get; set; }

        public int CompactWidth { get; set; }

        public int CompactHeight { get; set; }

        public static AppSettings Load(string path)
        {
            var settings = new AppSettings(path);
            try
            {
                if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json) is { } stored)
                {
                    settings.ShowStrip = stored.ShowStrip;
                    settings.StripTarget = string.IsNullOrEmpty(stored.StripTarget) ? StripTargets.Primary : stored.StripTarget;
                    settings.StripBackground = stored.StripBackground;
                    settings.AllowAgents = stored.AllowAgents;
                    settings.Compact = stored.Compact;
                    settings.AlwaysOnTop = stored.AlwaysOnTop;
                    settings.CompactShowsTasks = stored.CompactShowsTasks;
                    settings.NormalWidth = stored.NormalWidth;
                    settings.NormalHeight = stored.NormalHeight;
                    settings.CompactWidth = stored.CompactWidth;
                    settings.CompactHeight = stored.CompactHeight;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var temporary = _path + ".tmp";
                var stored = new Stored(ShowStrip, Compact, AlwaysOnTop, NormalWidth, NormalHeight, CompactWidth, CompactHeight, CompactShowsTasks, StripTarget, StripBackground, AllowAgents);
                File.WriteAllText(temporary, JsonSerializer.Serialize(stored, Json));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private sealed record Stored(
            bool ShowStrip = true,
            bool Compact = false,
            bool AlwaysOnTop = false,
            int NormalWidth = 0,
            int NormalHeight = 0,
            int CompactWidth = 0,
            int CompactHeight = 0,
            bool CompactShowsTasks = false,
            string StripTarget = StripTargets.Primary,
            bool StripBackground = true,
            bool AllowAgents = true);
    }
}
