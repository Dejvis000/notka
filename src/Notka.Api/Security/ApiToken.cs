using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace Notka.Api.Security
{
    public sealed record IssuedToken(string Value, string? FilePath);

    public static class ApiToken
    {
        public const int MinLength = 32;

        private const int MoveAttempts = 5;

        public static string DefaultPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "api-token");

        public static string GetOrCreate(IConfiguration configuration)
        {
            if (ExplicitToken(configuration) is { } explicitToken)
            {
                return explicitToken;
            }

            var path = ResolvePath(configuration);
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { } existing && existing.Length >= MinLength)
            {
                return existing;
            }

            return Write(path);
        }

        public static IssuedToken CreateNew(IConfiguration configuration)
        {
            if (ExplicitToken(configuration) is { } explicitToken)
            {
                return new IssuedToken(explicitToken, null);
            }

            var path = ResolvePath(configuration);
            return new IssuedToken(Write(path), path);
        }

        public static void Delete(IssuedToken token)
        {
            if (token.FilePath is not { } path)
            {
                return;
            }

            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Trim() == token.Value)
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static string ResolvePath(IConfiguration configuration) => configuration["Notka:TokenPath"] ?? DefaultPath();

        private static string? ExplicitToken(IConfiguration configuration)
        {
            if (configuration["Notka:Token"] is not { Length: > 0 } token)
            {
                return null;
            }

            if (token.Length < MinLength)
            {
                throw new InvalidOperationException($"Notka:Token must be at least {MinLength} characters.");
            }

            return token;
        }

        private static string Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, token);
            MoveWithRetry(temporary, path);
            return token;
        }

        private static void MoveWithRetry(string source, string destination)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(source, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (attempt < MoveAttempts && ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
