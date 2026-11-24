using System.Text.Json;

namespace Notka.Desktop.Services
{
    public sealed class AppSettings(string path)
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly string _path = path;

        public static string DefaultPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "settings.json");

        public bool ShowStrip { get; set; } = true;

        public static AppSettings Load(string path)
        {
            var settings = new AppSettings(path);
            try
            {
                if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json) is { } stored)
                {
                    settings.ShowStrip = stored.ShowStrip;
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
                File.WriteAllText(temporary, JsonSerializer.Serialize(new Stored(ShowStrip), Json));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private sealed record Stored(bool ShowStrip = true);
    }
}
