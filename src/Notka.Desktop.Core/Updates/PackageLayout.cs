namespace Notka.Desktop.Updates
{
    public static class PackageLayout
    {
        public const string Launcher = "Notka.exe";
        public const string AppFolder = "app";

        public static bool IsLayered(string appDirectory) =>
            string.Equals(Path.GetFileName(appDirectory), AppFolder, StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(appDirectory) is { } parent
            && File.Exists(Path.Combine(parent, Launcher));

        public static string Root(string appDirectory) =>
            IsLayered(appDirectory) ? Path.GetDirectoryName(appDirectory)! : appDirectory;

        public static bool IsDriveRoot(string path) =>
            Path.GetPathRoot(path) is { Length: > 0 } root
            && string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase);

        public static string[] RequiredFiles(bool layered) =>
            layered
                ? [Launcher, Path.Combine(AppFolder, "Notka.Desktop.exe"), Path.Combine(AppFolder, "Notka.Mcp.exe")]
                : ["Notka.Desktop.exe", "Notka.Mcp.exe"];
    }
}
