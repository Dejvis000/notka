using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Notka.Launcher
{
    internal static partial class Program
    {
        private const uint MbIconError = 0x10;

        [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int MessageBox(nint window, string text, string caption, uint type);

        private static int Main(string[] args)
        {
            var root = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var appDirectory = Path.Combine(root, "app");
            var exe = Path.Combine(appDirectory, "Notka.Desktop.exe");
            if (!File.Exists(exe))
            {
                MessageBox(0, "Nie znaleziono app\\Notka.Desktop.exe. Rozpakuj całą paczkę do jednego folderu.", "Notka", MbIconError);
                return 1;
            }

            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = appDirectory };
            foreach (var argument in args)
            {
                start.ArgumentList.Add(argument);
            }

            Process.Start(start);
            return 0;
        }
    }
}
