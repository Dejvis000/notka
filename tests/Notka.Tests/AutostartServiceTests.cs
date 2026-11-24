using System.Runtime.Versioning;
using Microsoft.Win32;
using Notka.Desktop.Services;

namespace Notka.Tests
{
    [SupportedOSPlatform("windows")]
    public sealed class AutostartServiceTests : IDisposable
    {
        private readonly string _keyPath = $@"Software\NotkaTests\{Guid.NewGuid():N}";

        public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\NotkaTests", throwOnMissingSubKey: false);

        [Fact]
        public void Autostart_can_be_enabled_and_disabled_with_a_quoted_tray_command()
        {
            var service = new AutostartService(@"C:\Program Files\Notka\Notka.Desktop.exe", _keyPath);
            Assert.False(service.IsEnabled);

            service.Enable();

            Assert.True(service.IsEnabled);
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath)!;
            Assert.Equal("\"C:\\Program Files\\Notka\\Notka.Desktop.exe\" --tray", key.GetValue("Notka"));

            service.Disable();
            Assert.False(service.IsEnabled);
        }

        [Fact]
        public void An_entry_pointing_to_another_exe_is_not_reported_as_enabled()
        {
            new AutostartService(@"C:\old\Notka.Desktop.exe", _keyPath).Enable();

            Assert.False(new AutostartService(@"C:\new\Notka.Desktop.exe", _keyPath).IsEnabled);
        }

        [Fact]
        public void Disabling_when_nothing_is_registered_does_not_throw()
        {
            new AutostartService(@"C:\x\Notka.Desktop.exe", _keyPath).Disable();
        }
    }
}
