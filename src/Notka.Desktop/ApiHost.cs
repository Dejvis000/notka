using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notka.Api.Extensions;
using Notka.Api.Security;
using Serilog;

namespace Notka.Desktop
{
    internal sealed class ApiHost
    {
        private WebApplication? _app;
        private IssuedToken? _issued;

        public Uri Url { get; private set; } = new("http://127.0.0.1:5123");

        public string Token => _issued?.Value ?? string.Empty;

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            _issued = ApiToken.CreateNew(builder.Configuration);
            try
            {
                builder.Configuration["Notka:Token"] = _issued.Value;
                var port = builder.Configuration.GetValue("Notka:Port", 5123);
                builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
                builder.Host.UseSerilog();
                builder.Services.AddNotkaApi(builder.Configuration);

                _app = builder.Build();
                _app.MapNotkaEndpoints();
                await _app.StartAsync();
                Url = new Uri($"http://127.0.0.1:{port}");
            }
            catch
            {
                ApiToken.Delete(_issued);
                if (_app is not null)
                {
                    await _app.DisposeAsync();
                    _app = null;
                }

                throw;
            }
        }

        public async Task StopAsync()
        {
            if (_issued is not null)
            {
                ApiToken.Delete(_issued);
            }

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
