using Notka.Desktop.Services;
using Notka.Desktop.Updates;
using Notka.Desktop.ViewModels;

namespace Notka.Tests
{
    public sealed class UpdateTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "notka-tests", $"updates-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            foreach (var file in new[] { _path, _path + ".tmp" }.Where(File.Exists))
            {
                File.Delete(file);
            }
        }

        [Theory]
        [InlineData("v0.2.0", "0.2.0")]
        [InlineData("0.2.0+abc123", "0.2.0")]
        [InlineData("1.4.7-beta.1", "1.4.7")]
        [InlineData("V2.0", "2.0.0")]
        public void Version_text_from_a_tag_or_assembly_is_normalized(string text, string expected) =>
            Assert.Equal(expected, AppVersion.Format(AppVersion.Parse(text)!));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("latest")]
        public void Unparseable_version_text_is_rejected(string? text) => Assert.Null(AppVersion.Parse(text));

        [Fact]
        public void Versions_compare_numerically_not_as_text()
        {
            Assert.True(AppVersion.Parse("v0.10.0")! > AppVersion.Parse("v0.9.0")!);
            Assert.True(AppVersion.IsDev(AppVersion.Parse("0.0.0")!));
        }

        [Fact]
        public void A_release_needs_both_the_package_and_its_checksum()
        {
            const string complete = """
                {"tag_name":"v0.2.0","body":"Notes","assets":[
                  {"name":"Notka-win-x64.zip","browser_download_url":"https://example.test/Notka-win-x64.zip"},
                  {"name":"Notka-win-x64.zip.sha256","browser_download_url":"https://example.test/Notka-win-x64.zip.sha256"}]}
                """;
            const string withoutChecksum = """
                {"tag_name":"v0.1.0","assets":[{"name":"Notka-Desktop-win-x64.zip","browser_download_url":"https://example.test/a.zip"}]}
                """;

            var release = GitHubUpdateFeed.Parse(complete);

            Assert.NotNull(release);
            Assert.Equal("0.2.0", AppVersion.Format(release!.Version));
            Assert.Equal("Notes", release.Notes);
            Assert.Null(GitHubUpdateFeed.Parse(withoutChecksum));
            Assert.Null(GitHubUpdateFeed.Parse("not json"));
            Assert.Null(GitHubUpdateFeed.Parse("""{"tag_name":"nightly","assets":[]}"""));
        }

        [Fact]
        public void The_expected_checksum_is_read_from_the_common_file_format()
        {
            var hash = new string('a', 64);

            Assert.Equal(hash, UpdateHash.ParseExpected($"{hash.ToUpperInvariant()}  Notka-win-x64.zip\n"));
            Assert.Null(UpdateHash.ParseExpected("abc123"));
            Assert.Null(UpdateHash.ParseExpected(new string('a', 65)));
        }

        [Fact]
        public async Task A_file_hash_matches_the_known_sha256()
        {
            var file = Path.Combine(Path.GetTempPath(), "notka-tests", $"hash-{Guid.NewGuid():N}.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "abc", TestContext.Current.CancellationToken);
            try
            {
                Assert.Equal(
                    "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                    await UpdateHash.ComputeAsync(file, TestContext.Current.CancellationToken));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void An_update_result_file_is_parsed_and_garbage_is_ignored()
        {
            var ok = UpdateResult.TryParse("""{"ok":true,"version":"0.2.0","message":""}""");
            var failed = UpdateResult.TryParse("""{"ok":false,"version":"0.2.0","message":"robocopy failed"}""");

            Assert.True(ok!.Ok);
            Assert.Equal("0.2.0", ok.Version);
            Assert.False(failed!.Ok);
            Assert.Null(UpdateResult.TryParse("nope"));
        }

        [Fact]
        public async Task A_newer_release_becomes_available_and_installing_hands_over_then_exits()
        {
            var installer = new FakeInstaller();
            var lifetime = new FakeLifetime();
            var updates = Create(new FakeFeed(Release("0.2.0")), installer, lifetime, current: "0.1.0");

            await updates.CheckAsync(manual: true);

            Assert.True(updates.HasUpdate);
            Assert.True(updates.CanInstall);
            Assert.Equal("Dostępna wersja 0.2.0", updates.AvailableText);

            await updates.InstallCommand.ExecuteAsync(null);

            Assert.Equal("0.2.0", AppVersion.Format(installer.Staged!.Version));
            Assert.Equal(1, lifetime.ExitRequests);
        }

        [Fact]
        public async Task Installing_shows_progress_and_tells_the_user_the_app_will_restart()
        {
            var updates = Create(new FakeFeed(Release("0.2.0")), new FakeInstaller(), new FakeLifetime(), current: "0.1.0");
            await updates.CheckAsync(manual: true);
            var changes = new List<string>();
            updates.PropertyChanged += (sender, args) => changes.Add(args.PropertyName!);
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new InlineContext());
            try
            {
                await updates.InstallCommand.ExecuteAsync(null);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Assert.True(updates.IsInstalling);
            Assert.True(updates.ShowBanner);
            Assert.Contains("uruchomi się ponownie", updates.BannerText);
            Assert.Contains(nameof(UpdateViewModel.ProgressValue), changes);
            Assert.True(updates.ProgressIsIndeterminate);
        }

        [Fact]
        public async Task An_exit_the_app_refuses_leaves_the_update_retryable_with_an_explanation()
        {
            var lifetime = new FakeLifetime { CanExit = false };
            var updates = Create(new FakeFeed(Release("0.2.0")), new FakeInstaller(), lifetime, current: "0.1.0");
            await updates.CheckAsync(manual: true);

            await updates.InstallCommand.ExecuteAsync(null);

            Assert.False(updates.IsBusy);
            Assert.False(updates.IsInstalling);
            Assert.Contains("niezapisane zmiany", updates.FailureMessage);
            Assert.True(updates.CanInstall);
        }

        [Fact]
        public async Task An_unexpected_installer_error_still_resets_the_state()
        {
            var installer = new FakeInstaller { Failure = new UnauthorizedAccessException("denied") };
            var updates = Create(new FakeFeed(Release("0.2.0")), installer, new FakeLifetime(), current: "0.1.0");
            await updates.CheckAsync(manual: true);

            await updates.InstallCommand.ExecuteAsync(null);

            Assert.False(updates.IsBusy);
            Assert.False(updates.IsInstalling);
            Assert.Contains("Nie udało się zainstalować", updates.FailureMessage);
        }

        [Fact]
        public async Task A_blocked_install_explains_why_in_the_banner()
        {
            var installer = new FakeInstaller { Reason = "Folder aplikacji jest tylko do odczytu." };
            var updates = Create(new FakeFeed(Release("0.2.0")), installer, new FakeLifetime(), current: "0.1.0");

            await updates.CheckAsync(manual: true);

            Assert.False(updates.CanInstall);
            Assert.Contains("tylko do odczytu", updates.BannerText);
        }

        [Fact]
        public void A_failed_update_is_described_by_what_actually_happened()
        {
            var updates = Create(new FakeFeed(null), new FakeInstaller(), new FakeLifetime(), current: "0.1.0");

            updates.ShowResult(new UpdateResult(false, "0.2.0", "x", Attempted: false));
            Assert.Contains("nie zamknęła się na czas", updates.FailureMessage);

            updates.ShowResult(new UpdateResult(false, "0.2.0", "x", Attempted: true, Restored: true));
            Assert.Contains("przywrócono poprzednią wersję", updates.FailureMessage);

            updates.ShowResult(new UpdateResult(false, "0.2.0", "x", Attempted: true, Restored: false));
            Assert.Contains("nie udało się w pełni przywrócić", updates.FailureMessage);
            Assert.Null(updates.SuccessMessage);
        }

        [Fact]
        public void The_new_result_flags_are_read_with_safe_defaults()
        {
            var full = UpdateResult.TryParse("""{"ok":false,"version":"0.2.0","message":"m","attempted":false,"restored":true}""");
            var legacy = UpdateResult.TryParse("""{"ok":false,"version":"0.2.0","message":"m"}""");

            Assert.False(full!.Attempted);
            Assert.True(full.Restored);
            Assert.True(legacy!.Attempted);
            Assert.False(legacy.Restored);
        }

        [Fact]
        public async Task A_failed_install_hides_the_installing_banner_state()
        {
            var installer = new FakeInstaller { Failure = new UpdateException("Nie udało się.") };
            var updates = Create(new FakeFeed(Release("0.2.0")), installer, new FakeLifetime(), current: "0.1.0");
            await updates.CheckAsync(manual: true);

            await updates.InstallCommand.ExecuteAsync(null);

            Assert.False(updates.IsInstalling);
            Assert.Equal(updates.AvailableText, updates.BannerText);
        }

        [Fact]
        public async Task The_same_or_an_older_release_is_not_offered()
        {
            var updates = Create(new FakeFeed(Release("0.1.0")), new FakeInstaller(), new FakeLifetime(), current: "0.1.0");

            await updates.CheckAsync(manual: true);

            Assert.False(updates.HasUpdate);
            Assert.Equal("Masz najnowszą wersję.", updates.Status);
        }

        [Fact]
        public async Task A_release_without_a_package_is_reported_only_for_a_manual_check()
        {
            var manual = Create(new FakeFeed(null), new FakeInstaller(), new FakeLifetime(), current: "0.1.0");
            var automatic = Create(new FakeFeed(null), new FakeInstaller(), new FakeLifetime(), current: "0.1.0");

            await manual.CheckAsync(manual: true);
            await automatic.CheckAsync(manual: false);

            Assert.Contains("Brak wydania", manual.Status);
            Assert.Null(automatic.Status);
        }

        [Fact]
        public async Task A_network_failure_is_reported_for_a_manual_check_and_silent_otherwise()
        {
            var feed = new FakeFeed(null) { Failure = new HttpRequestException("offline") };
            var manual = Create(feed, new FakeInstaller(), new FakeLifetime(), current: "0.1.0");
            var automatic = Create(feed, new FakeInstaller(), new FakeLifetime(), current: "0.1.0");

            await manual.CheckAsync(manual: true);
            await automatic.CheckAsync(manual: false);

            Assert.Contains("Nie udało się sprawdzić", manual.Status);
            Assert.Null(automatic.Status);
            Assert.False(manual.IsBusy);
        }

        [Fact]
        public async Task A_development_build_never_installs()
        {
            var installer = new FakeInstaller { Reason = "Wersja deweloperska nie aktualizuje się sama." };
            var lifetime = new FakeLifetime();
            var updates = Create(new FakeFeed(Release("0.2.0")), installer, lifetime, current: "0.0.0");

            await updates.CheckAsync(manual: true);
            await updates.InstallCommand.ExecuteAsync(null);

            Assert.True(updates.IsDevelopmentBuild);
            Assert.False(updates.CanInstall);
            Assert.Null(installer.Staged);
            Assert.Equal(0, lifetime.ExitRequests);
            Assert.Contains("deweloperska", updates.Status);
        }

        [Fact]
        public async Task A_failed_install_shows_the_reason_and_keeps_the_app_running()
        {
            var installer = new FakeInstaller { Failure = new UpdateException("Suma kontrolna paczki się nie zgadza.") };
            var lifetime = new FakeLifetime();
            var updates = Create(new FakeFeed(Release("0.2.0")), installer, lifetime, current: "0.1.0");
            await updates.CheckAsync(manual: true);

            await updates.InstallCommand.ExecuteAsync(null);

            Assert.Equal("Suma kontrolna paczki się nie zgadza.", updates.FailureMessage);
            Assert.Equal(0, lifetime.ExitRequests);
            Assert.False(updates.IsBusy);
        }

        [Fact]
        public void The_automatic_check_is_on_by_default_and_the_choice_is_remembered()
        {
            var updates = Create(new FakeFeed(null), new FakeInstaller(), new FakeLifetime(), current: "0.1.0");
            Assert.True(updates.AutoCheck);

            updates.AutoCheck = false;

            Assert.False(Create(new FakeFeed(null), new FakeInstaller(), new FakeLifetime(), current: "0.1.0").AutoCheck);
        }

        [Fact]
        public void The_result_of_the_previous_update_is_explained()
        {
            var updates = Create(new FakeFeed(null), new FakeInstaller(), new FakeLifetime(), current: "0.2.0");

            updates.ShowResult(new UpdateResult(true, "0.2.0", null));
            Assert.Contains("zaktualizowana do wersji 0.2.0", updates.ResultMessage);
            Assert.Contains("Claude Desktop", updates.ResultMessage);

            updates.ShowResult(new UpdateResult(false, "0.2.0", "x", Restored: true));
            Assert.Contains("przywrócono poprzednią wersję", updates.ResultMessage);
        }

        private static ReleaseInfo Release(string version) => new(
            AppVersion.Parse(version)!,
            $"v{version}",
            new Uri("https://example.test/Notka-win-x64.zip"),
            new Uri("https://example.test/Notka-win-x64.zip.sha256"),
            string.Empty);

        private UpdateViewModel Create(IUpdateFeed feed, IUpdateInstaller installer, IAppLifetime lifetime, string current) =>
            new(feed, installer, lifetime, AppSettings.Load(_path), AppVersion.Parse(current)!, TimeSpan.Zero);

        private sealed class InlineContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => d(state);
        }

        private sealed class FakeFeed(ReleaseInfo? release) : IUpdateFeed
        {
            public Exception? Failure { get; set; }

            public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) =>
                Failure is { } failure ? Task.FromException<ReleaseInfo?>(failure) : Task.FromResult(release);
        }

        private sealed class FakeInstaller : IUpdateInstaller
        {
            public string? Reason { get; set; }

            public Exception? Failure { get; set; }

            public ReleaseInfo? Staged { get; private set; }

            public string? UnavailableReason => Reason;

            public Task StageAsync(ReleaseInfo release, IProgress<UpdateStep> progress, CancellationToken cancellationToken)
            {
                progress.Report(new UpdateStep("Pobieranie aktualizacji… 40%", 40));
                if (Failure is { } failure)
                {
                    return Task.FromException(failure);
                }

                Staged = release;
                return Task.CompletedTask;
            }

            public void LaunchStaged()
            {
            }
        }

        private sealed class FakeLifetime : IAppLifetime
        {
            public int ExitRequests { get; private set; }

            public bool CanExit { get; set; } = true;

            public void RequestExit() => ExitRequests++;

            public Task<bool> TryExitAsync()
            {
                ExitRequests++;
                return Task.FromResult(CanExit);
            }
        }
    }
}
