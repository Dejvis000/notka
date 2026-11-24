using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notka.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var apiUrl = new Uri(builder.Configuration["Notka:ApiUrl"] ?? "http://127.0.0.1:5123");
if (!IPAddress.TryParse(apiUrl.Host, out var apiAddress) || !IPAddress.IsLoopback(apiAddress))
{
    throw new InvalidOperationException("Notka:ApiUrl must use a loopback IP address (127.0.0.1 or [::1]), not a host name.");
}

builder.Services.AddTransient<TokenHandler>();
builder.Services.AddHttpClient<NotkaApiClient>(client =>
{
    client.BaseAddress = apiUrl;
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<TokenHandler>().RedactLoggedHeaders(["Authorization"]);

builder.Services
    .AddMcpServer(options => options.ServerInstructions =
        "Notka is the user's personal task and sticky note app. Dates and times are the user's local time. " +
        "Tasks are referred to by numeric id: look it up with search_tasks or list_tasks before completing or updating a task.")
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
