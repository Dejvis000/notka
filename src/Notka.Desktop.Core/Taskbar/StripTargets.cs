namespace Notka.Desktop.Taskbar
{
    public static class StripTargets
    {
        public const string Primary = "primary";
        public const string All = "all";
    }

    public sealed record StripTargetOption(string Id, string Label);

    public interface IMonitorLister
    {
        IReadOnlyList<StripTargetOption> List();
    }
}
