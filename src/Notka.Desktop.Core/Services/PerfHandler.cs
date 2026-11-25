using System.Text.RegularExpressions;
using Notka.Core;

namespace Notka.Desktop.Services
{
    public sealed partial class PerfHandler : DelegatingHandler
    {
        public static string Normalize(string? path) => Numbers().Replace(path ?? string.Empty, "/{id}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var scope = Perf.Measure($"http {request.Method} {Normalize(request.RequestUri?.AbsolutePath)}");
            return await base.SendAsync(request, cancellationToken);
        }

        [GeneratedRegex("/[0-9]+(?=/|$)")]
        private static partial Regex Numbers();
    }
}
