using Notka.Desktop.Services;
using Serilog;
using Windows.ApplicationModel.DataTransfer;

namespace Notka.Desktop
{
    internal sealed class AgentConnector : IAgentConnector
    {
        public string ServerPath { get; } = Path.Combine(AppContext.BaseDirectory, "Notka.Mcp.exe");

        public bool ServerExists => File.Exists(ServerPath);

        public void Copy(string text)
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            try
            {
                Clipboard.Flush();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not flush the clipboard");
            }
        }
    }
}
