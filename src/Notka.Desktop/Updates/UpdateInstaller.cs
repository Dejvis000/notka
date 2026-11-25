using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Serilog;

namespace Notka.Desktop.Updates
{
    internal sealed class UpdateInstaller(HttpClient http, Version current) : IUpdateInstaller
    {
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        private readonly HttpClient _http = http;
        private readonly Version _current = current;
        private ReleaseInfo? _staged;

        public static string WorkDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "update");

        public static string ResultPath { get; } = Path.Combine(WorkDirectory, "result.json");

        public string? UnavailableReason
        {
            get
            {
                if (AppVersion.IsDev(_current))
                {
                    return "Wersja deweloperska nie aktualizuje się sama.";
                }

                var app = AppDirectory();
                if (string.Equals(Path.GetPathRoot(app), AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return "Notka leży w katalogu głównym dysku. Przenieś ją do własnego folderu, aby aktualizować automatycznie.";
                }

                if (IsInside(WorkDirectory, app) || IsInside(app, WorkDirectory))
                {
                    return "Folder aplikacji pokrywa się z folderem danych Notki. Przenieś aplikację do osobnego folderu, aby aktualizować automatycznie.";
                }

                return IsWritable(app)
                    ? null
                    : "Folder aplikacji jest tylko do odczytu. Pobierz nową wersję ręcznie z GitHub Releases.";
            }
        }

        public static bool IsTrusted(Uri url) =>
            url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback);

        public async Task StageAsync(ReleaseInfo release, IProgress<UpdateStep> progress, CancellationToken cancellationToken)
        {
            if (UnavailableReason is { } reason)
            {
                throw new UpdateException(reason);
            }

            if (!IsTrusted(release.PackageUrl) || !IsTrusted(release.HashUrl))
            {
                throw new UpdateException("Adres paczki aktualizacji nie jest bezpieczny. Aktualizacja przerwana.");
            }

            _staged = null;
            await Task.Run(ResetWorkDirectory, cancellationToken);
            var zip = Path.Combine(WorkDirectory, GitHubUpdateFeed.PackageName);
            await DownloadAsync(release.PackageUrl, zip, progress, cancellationToken);

            progress.Report(new UpdateStep("Sprawdzanie sumy kontrolnej…", null));
            var expected = UpdateHash.ParseExpected(await _http.GetStringAsync(release.HashUrl, cancellationToken))
                ?? throw new UpdateException("Plik z sumą kontrolną jest nieprawidłowy. Aktualizacja przerwana.");
            if (await UpdateHash.ComputeAsync(zip, cancellationToken) != expected)
            {
                throw new UpdateException("Suma kontrolna paczki się nie zgadza. Aktualizacja przerwana.");
            }

            progress.Report(new UpdateStep("Rozpakowywanie aktualizacji…", null));
            var staging = Path.Combine(WorkDirectory, "staging");
            try
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(zip, staging), cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new UpdateException("Nie udało się rozpakować paczki aktualizacji.", ex);
            }

            foreach (var exe in new[] { "Notka.Desktop.exe", "Notka.Mcp.exe" })
            {
                if (!File.Exists(Path.Combine(staging, exe)))
                {
                    throw new UpdateException($"Paczka aktualizacji jest niekompletna (brak {exe}).");
                }
            }

            await File.WriteAllTextAsync(
                Path.Combine(WorkDirectory, "apply.ps1"),
                UpdateScript.Text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);
            _staged = release;
        }

        public void LaunchStaged()
        {
            if (_staged is not { } release)
            {
                return;
            }

            _staged = null;
            var exe = Environment.ProcessPath ?? Path.Combine(AppDirectory(), "Notka.Desktop.exe");
            var start = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(WorkDirectory, "apply.ps1"),
                "-ProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-Source", Path.Combine(WorkDirectory, "staging"),
                "-Target", AppDirectory(),
                "-Exe", exe,
                "-Version", AppVersion.Format(release.Version),
                "-Work", WorkDirectory,
            })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                Process.Start(start);
                Log.Information("Update to {Version} staged; handing over to the updater", release.Tag);
            }
            catch (Win32Exception ex)
            {
                Log.Error(ex, "Could not start the updater");
            }
        }

        private static string AppDirectory() => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        private static bool IsInside(string child, string parent)
        {
            var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWritable(string directory)
        {
            try
            {
                var probe = Path.Combine(directory, $".notka-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void ResetWorkDirectory()
        {
            if (Directory.Exists(WorkDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(WorkDirectory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(WorkDirectory, recursive: true);
            }

            Directory.CreateDirectory(WorkDirectory);
        }

        private async Task DownloadAsync(Uri url, string path, IProgress<UpdateStep> progress, CancellationToken cancellationToken)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var target = File.Create(path);
                var buffer = new byte[81920];
                long received = 0;
                var lastReported = -2;
                int read;
                while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(StallTimeout);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    var percent = total is > 0 ? (int)(received * 100 / total.Value) : -1;
                    if (percent != lastReported)
                    {
                        lastReported = percent;
                        progress.Report(new UpdateStep(
                            percent < 0 ? "Pobieranie aktualizacji…" : $"Pobieranie aktualizacji… {percent}%",
                            percent < 0 ? null : percent));
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateException("Pobieranie zostało przerwane, bo serwer przestał wysyłać dane. Spróbuj ponownie.");
            }
        }
    }
}
