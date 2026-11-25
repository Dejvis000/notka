namespace Notka.Desktop.Taskbar
{
    internal sealed class MonitorLister : IMonitorLister
    {
        public IReadOnlyList<StripTargetOption> List()
        {
            var monitors = new List<StripTargetOption>();
            StripNative.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr monitor, IntPtr dc, ref StripNative.Rect rect, IntPtr data) =>
                {
                    if (StripNative.InfoOf(monitor) is { } info)
                    {
                        var number = new string(info.Device.Where(char.IsDigit).ToArray());
                        var size = $"{info.Monitor.Right - info.Monitor.Left}×{info.Monitor.Bottom - info.Monitor.Top}";
                        var primary = (info.Flags & StripNative.MonitorPrimary) != 0 ? ", główny" : string.Empty;
                        monitors.Add(new StripTargetOption(info.Device, $"Monitor {number} ({size}{primary})"));
                    }

                    return true;
                },
                IntPtr.Zero);
            return monitors.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        }
    }
}
