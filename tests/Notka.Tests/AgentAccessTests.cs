using Notka.Desktop.Services;
using Notka.Desktop.Taskbar;
using Notka.Desktop.ViewModels;

namespace Notka.Tests
{
    public sealed class AgentAccessTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "notka-tests", $"agents-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            foreach (var file in new[] { _path, _path + ".tmp" }.Where(File.Exists))
            {
                File.Delete(file);
            }
        }

        private SettingsViewModel Create(IAgentConnector? agents = null) =>
            new(new NoAutostart(), new NoLifetime(), AppSettings.Load(_path), null, agents);

        [Fact]
        public void Agents_are_allowed_by_default_and_the_choice_is_saved_and_announced()
        {
            var settings = Create();
            var announced = new List<bool>();
            settings.AllowAgentsChanged += announced.Add;
            Assert.True(settings.AllowAgents);

            settings.AllowAgents = false;

            Assert.Equal([false], announced);
            Assert.False(Create().AllowAgents);
        }

        [Fact]
        public void The_prompt_names_the_server_and_quotes_its_path()
        {
            var prompt = AgentPrompt.Build(@"C:\Program Files\Notka\Notka.Mcp.exe");

            Assert.Contains("\"notka\"", prompt);
            Assert.Contains("\"C:\\Program Files\\Notka\\Notka.Mcp.exe\"", prompt);
            Assert.Contains("stdio", prompt);
        }

        [Fact]
        public void Copying_the_prompt_puts_it_on_the_clipboard_and_confirms()
        {
            var agents = new FakeAgents(exists: true);
            var settings = Create(agents);

            settings.CopyAgentPromptCommand.Execute(null);

            Assert.Equal(AgentPrompt.Build(agents.ServerPath), agents.Copied);
            Assert.Contains("Skopiowano", settings.AgentMessage);
        }

        [Fact]
        public void A_clipboard_failure_is_reported_and_replaces_an_earlier_success_message()
        {
            var agents = new FakeAgents(exists: true);
            var settings = Create(agents);
            settings.CopyAgentPromptCommand.Execute(null);
            Assert.Contains("Skopiowano", settings.AgentMessage);

            agents.FailNextCopy = true;
            settings.CopyAgentPromptCommand.Execute(null);

            Assert.Contains("Nie udało się", settings.AgentMessage);
        }

        [Fact]
        public void A_missing_server_executable_is_reported_instead_of_copying()
        {
            var agents = new FakeAgents(exists: false);
            var settings = Create(agents);

            settings.CopyAgentPromptCommand.Execute(null);

            Assert.Null(agents.Copied);
            Assert.Contains("Nie znaleziono", settings.AgentMessage);
        }

        private sealed class FakeAgents(bool exists) : IAgentConnector
        {
            public string ServerPath => @"C:\Notka\Notka.Mcp.exe";

            public bool ServerExists => exists;

            public string? Copied { get; private set; }

            public bool FailNextCopy { get; set; }

            public void Copy(string text)
            {
                if (FailNextCopy)
                {
                    throw new InvalidOperationException("clipboard busy");
                }

                Copied = text;
            }
        }

        private sealed class NoAutostart : IAutostart
        {
            public bool IsEnabled { get; private set; }

            public void Enable() => IsEnabled = true;

            public void Disable() => IsEnabled = false;
        }

        private sealed class NoLifetime : IAppLifetime
        {
            public void RequestExit()
            {
            }
        }
    }
}
