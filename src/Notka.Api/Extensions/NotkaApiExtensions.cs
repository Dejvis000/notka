using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Notka.Api.Data;
using Notka.Api.Endpoints;
using Notka.Api.ErrorHandling;
using Notka.Api.Hubs;
using Notka.Api.Middleware;
using Notka.Api.Security;
using Notka.Core;

namespace Notka.Api.Extensions
{
    public static class NotkaApiExtensions
    {
        /// <summary>Registers the database, services, SignalR and migrate-on-start.</summary>
        public static IServiceCollection AddNotkaApi(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("Notka") ?? $"Data Source={ResolveDbPath(configuration)}";

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(new ApiTokenHolder(ApiToken.GetOrCreate(configuration)));
            services.AddSingleton<AgentAccess>();
            services.AddDbContext<NotkaDbContext>(o => o.UseSqlite(connectionString));
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            services.AddApplicationServices(typeof(NotkaApiExtensions).Assembly);
            services.AddSignalR();
            services.AddProblemDetails();
            services.AddExceptionHandler<ApiExceptionHandler>();
            services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            services.AddHostedService<MigrationService>();
            return services;
        }

        /// <summary>Adds the exception handler and maps the REST endpoints and the SignalR hub.</summary>
        public static WebApplication MapNotkaEndpoints(this WebApplication app)
        {
            if (app.Services.GetService<PerfRecorder>() is { } recorder)
            {
                app.UseMiddleware<PerfMiddleware>(recorder);
            }

            app.UseExceptionHandler();
            app.UseStatusCodePages();
            app.UseMiddleware<LocalRequestsOnlyMiddleware>();
            app.UseMiddleware<ApiTokenMiddleware>();
            app.UseMiddleware<AgentAccessMiddleware>();
            TaskEndpoints.Map(app);
            NoteEndpoints.Map(app);
            app.MapHub<ChangesHub>(ChangesHub.Path);
            return app;
        }

        private static string ResolveDbPath(IConfiguration configuration)
        {
            var path = configuration["Notka:DbPath"]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "notka.db");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            return path;
        }

        private sealed class MigrationService(IServiceProvider services) : IHostedService
        {
            private readonly IServiceProvider _services = services;

            public async Task StartAsync(CancellationToken cancellationToken)
            {
                await using var scope = _services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotkaDbContext>().Database.MigrateAsync(cancellationToken);
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
