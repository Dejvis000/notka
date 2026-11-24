using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Notka.Desktop.Services
{
    public interface IAutostart
    {
        bool IsEnabled { get; }

        void Enable();

        void Disable();
    }

    [SupportedOSPlatform("windows")]
    public sealed class AutostartService(string exePath, string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run") : IAutostart
    {
        public const string TrayArgument = "--tray";

        private const string ValueName = "Notka";

        private readonly string _runKeyPath = runKeyPath;
        private readonly string _command = $"\"{exePath}\" {TrayArgument}";

        public bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
                return key?.GetValue(ValueName) is string value && value == _command;
            }
        }

        public void Enable()
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
            key.SetValue(ValueName, _command);
        }

        public void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
