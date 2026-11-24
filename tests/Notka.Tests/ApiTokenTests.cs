using Microsoft.Extensions.Configuration;
using Notka.Api.Security;

namespace Notka.Tests
{
    public sealed class ApiTokenTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "notka-tests", Guid.NewGuid().ToString("N"));

        private string TokenPath => Path.Combine(_directory, "api-token");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static IConfiguration Config(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

        [Fact]
        public void A_token_file_is_created_once_and_reused()
        {
            var config = Config(("Notka:TokenPath", TokenPath));

            var first = ApiToken.GetOrCreate(config);
            var second = ApiToken.GetOrCreate(config);

            Assert.Equal(first, second);
            Assert.True(first.Length >= ApiToken.MinLength);
            Assert.Matches("^[A-Za-z0-9_-]+$", first);
            Assert.Equal(first, File.ReadAllText(TokenPath));
        }

        [Fact]
        public void Different_installations_get_different_tokens()
        {
            var one = ApiToken.GetOrCreate(Config(("Notka:TokenPath", TokenPath)));
            var other = ApiToken.GetOrCreate(Config(("Notka:TokenPath", Path.Combine(_directory, "other-token"))));

            Assert.NotEqual(one, other);
        }

        [Fact]
        public void A_corrupt_or_too_short_token_file_is_replaced()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(TokenPath, "short");

            var token = ApiToken.GetOrCreate(Config(("Notka:TokenPath", TokenPath)));

            Assert.True(token.Length >= ApiToken.MinLength);
            Assert.Equal(token, File.ReadAllText(TokenPath));
        }

        [Fact]
        public void A_new_token_replaces_the_previous_one_each_time()
        {
            var config = Config(("Notka:TokenPath", TokenPath));

            var first = ApiToken.CreateNew(config);
            var second = ApiToken.CreateNew(config);

            Assert.NotEqual(first.Value, second.Value);
            Assert.Equal(second.Value, File.ReadAllText(TokenPath));
            Assert.False(File.Exists(TokenPath + ".tmp"));
        }

        [Fact]
        public async Task Rotating_waits_for_a_reader_that_briefly_holds_the_file()
        {
            var config = Config(("Notka:TokenPath", TokenPath));
            var first = ApiToken.CreateNew(config);
            var reader = new FileStream(TokenPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                reader.Dispose();
            }, TestContext.Current.CancellationToken);

            var second = ApiToken.CreateNew(config);

            Assert.NotEqual(first.Value, second.Value);
            Assert.Equal(second.Value, File.ReadAllText(TokenPath));
        }

        [Fact]
        public void Deleting_a_token_removes_only_the_file_that_still_holds_it()
        {
            var config = Config(("Notka:TokenPath", TokenPath));
            var issued = ApiToken.CreateNew(config);

            ApiToken.Delete(new IssuedToken("another-token-value", issued.FilePath));
            Assert.True(File.Exists(TokenPath));

            ApiToken.Delete(issued);
            Assert.False(File.Exists(TokenPath));
        }

        [Fact]
        public void An_explicit_token_is_never_written_or_deleted()
        {
            var explicitToken = new string('b', ApiToken.MinLength);
            var issued = ApiToken.CreateNew(Config(("Notka:Token", explicitToken), ("Notka:TokenPath", TokenPath)));

            ApiToken.Delete(issued);

            Assert.Equal(explicitToken, issued.Value);
            Assert.Null(issued.FilePath);
            Assert.False(File.Exists(TokenPath));
        }

        [Fact]
        public void An_explicit_token_wins_and_creates_no_file()
        {
            var explicitToken = new string('a', ApiToken.MinLength);

            var token = ApiToken.GetOrCreate(Config(("Notka:Token", explicitToken), ("Notka:TokenPath", TokenPath)));

            Assert.Equal(explicitToken, token);
            Assert.False(File.Exists(TokenPath));
        }

        [Fact]
        public void A_weak_explicit_token_is_rejected()
        {
            Assert.Throws<InvalidOperationException>(() => ApiToken.GetOrCreate(Config(("Notka:Token", "too-short"))));
        }
    }
}
