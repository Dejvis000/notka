using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace Notka.Mcp
{
    internal sealed class TokenHandler(IConfiguration configuration) : DelegatingHandler
    {
        private readonly IConfiguration _configuration = configuration;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken());
            return base.SendAsync(request, cancellationToken);
        }

        private string ReadToken()
        {
            if (_configuration["Notka:Token"] is { Length: > 0 } token)
            {
                return token;
            }

            var path = _configuration["Notka:TokenPath"]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "api-token");
            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new HttpRequestException("The Notka token file is not available.", ex);
            }
        }
    }
}
