namespace Notka.Desktop.Services
{
    public interface IAgentConnector
    {
        string ServerPath { get; }

        bool ServerExists { get; }

        void Copy(string text);
    }

    public static class AgentPrompt
    {
        public const string ServerName = "notka";

        public static string Build(string serverPath) =>
            $"Add a stdio MCP server named \"{ServerName}\" that runs the command \"{serverPath}\" to my user-level MCP configuration on this PC. " +
            "If you cannot configure MCP servers directly, show me where to add it manually. " +
            "The Notka app must be running for its tools to work.";
    }
}
