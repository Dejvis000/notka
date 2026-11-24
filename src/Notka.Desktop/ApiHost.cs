using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notka.Api.Extensions;
using Serilog;

namespace Notka.Desktop
{
    internal sealed class ApiHost
    {
        private WebApplication? _app;

        public Uri Url { get; private set; } = new("http://127.0.0.1:5123");

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            var port = builder.Configuration.GetValue("Notka:Port", 5123);
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Host.UseSerilog();
            builder.Services.AddNotkaApi(builder.Configuration);

            _app = builder.Build();
            _app.MapNotkaEndpoints();
            await _app.StartAsync();
            Url = new Uri($"http://127.0.0.1:{port}");
        }

        public async Task StopAsync()
        {
            if (_app is null)
            {
                return;
            }

            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
