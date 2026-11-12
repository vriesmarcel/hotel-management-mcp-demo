using System.Net;
using Hotel.Application;
using Hotel.Sql;
using Hotel.Mcp.Host;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
// Loopback only: the fixed token is a presentation convenience, not production authentication.
var port = int.Parse(Environment.GetEnvironmentVariable("HMS_MCP_PORT") ?? "5057");
if (port < 1024 || port > 65535) throw new InvalidOperationException("Invalid demo port.");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var connection = Environment.GetEnvironmentVariable("HMS_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("HotelDatabase")
    ?? throw new InvalidOperationException("Run scripts/Initialize-Demo03.ps1 to configure the demo database.");
var store = new SqlHotelStore(connection);
var catalogs = new ContextCatalogs(new ReservationsService(store), new StaysService(store),
    new BillingService(store), new HotelServices(store));
builder.Services.AddMcpServer().WithHttpTransport(options =>
{
    options.SessionMode = HttpServerSessionMode.Stateless;
    // The SDK creates fresh server options for every stateless request.
    // Never register a global union of tools: each route gets its own catalog.
    options.ConfigureSessionOptions = (http, server, cancellationToken) =>
    {
        var context = catalogs.ForPath(http.Request.Path.Value!);
        server.ServerInfo = new Implementation { Name = context.Name, Version = "3.0.0" };
        server.ServerInstructions = context.Instructions;
        server.ToolCollection = context.Tools;
        server.ResourceCollection = context.Resources;
        server.PromptCollection = context.Prompts;
        server.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = context.Resources.Count > 0 ? new ResourcesCapability() : null,
            Prompts = context.Prompts.Count > 0 ? new PromptsCapability() : null
        };
        return Task.CompletedTask;
    };
});
var app = builder.Build();
app.Use(async (http, next) =>
{
    if (http.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)
        || (http.Request.Host.Host != "127.0.0.1" && http.Request.Host.Host != "localhost"))
    { http.Response.StatusCode = 403; return; }
    if (http.Request.Headers.TryGetValue("Origin", out var origin)
        && origin != $"http://127.0.0.1:{port}" && origin != $"http://localhost:{port}")
    { http.Response.StatusCode = 403; return; }
    if (http.Request.Headers.Authorization != "Bearer 123456")
    { http.Response.StatusCode = 401; http.Response.Headers.WWWAuthenticate = "Bearer"; return; }
    await next(http);
});
app.MapMcp("/mcp/reservations");
app.MapMcp("/mcp/stays");
app.MapMcp("/mcp/billing");
app.Run();
