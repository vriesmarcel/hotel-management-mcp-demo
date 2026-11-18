using System.Net;
using Hotel.Application;
using Hotel.Sql;
using Hotel.Mcp.Host;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

const string PreviewPath = "/demo/reservations";
const string DemoToken = "Bearer 123456";
const string DemoPreviewContentType = "text/html; charset=utf-8";
const string DemoPreviewCsp = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-src 'self' about:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true)
                     .AddEnvironmentVariables();

// Loopback only: the fixed token is a presentation convenience, not production authentication.
var port = GetDemoPort();

builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

var connectionString = Environment.GetEnvironmentVariable("HMS_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("HotelDatabase")
    ?? throw new InvalidOperationException("Run scripts/Initialize-Demo03.ps1 to configure the demo database.");
var store = new SqlHotelStore(connectionString);
var catalogs = new ContextCatalogs(
    new ReservationsService(store),
    new StaysService(store),
    new BillingService(store), new HotelServices(store));

ConfigureMcpServer(builder.Services, catalogs);

var app = builder.Build();
var logger = app.Logger;
var appStoppingToken = app.Lifetime.ApplicationStopping;

app.Use(async (http, next) =>
{
    if (!IsLoopbackOnlyRequest(http))
    {
        http.Response.StatusCode = 403;
        return;
    }

    if (!IsAllowedOrigin(http, port))
    {
        http.Response.StatusCode = 403;
        return;
    }

    // Only the local reference host page is public; MCP calls still require the demo token.
    if (!IsPreviewRequest(http) && !HasDemoToken(http))
    {
        http.Response.StatusCode = 401;
        http.Response.Headers.WWWAuthenticate = "Bearer";
        return;
    }

    try
    {
        await next(http);
    }
    catch (OperationCanceledException ex)
    {
        var reason = appStoppingToken.IsCancellationRequested
            ? "application stopping"
            : http.RequestAborted.IsCancellationRequested
                ? "request aborted (client disconnected or request canceled)"
                : "unknown cancellation source";

        logger.LogInformation(ex,
            "Request canceled. Method={Method} Path={Path} TraceId={TraceId} Reason={Reason} RemoteIp={RemoteIp}",
            http.Request.Method,
            http.Request.Path,
            http.TraceIdentifier,
            reason,
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return;
    }
});
app.MapGet(PreviewPath, (HttpContext http) =>
{
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers["Content-Security-Policy"] = DemoPreviewCsp;
    return Results.Content(ReservationApp.Preview(), DemoPreviewContentType);
});
app.MapMcp("/mcp/reservations");
app.MapMcp("/mcp/stays");
app.MapMcp("/mcp/billing");
app.Run();

int GetDemoPort()
{
    var port = int.Parse(Environment.GetEnvironmentVariable("HMS_MCP_PORT") ?? "5057");
    if (port < 1024 || port > 65535)
    {
        throw new InvalidOperationException("Invalid demo port.");
    }

    return port;
}

void ConfigureMcpServer(IServiceCollection services, ContextCatalogs routeCatalogs)
{
    services.AddMcpServer().WithHttpTransport(options =>
    {
        options.SessionMode = HttpServerSessionMode.Stateless;

        // The SDK creates fresh server options for every stateless request.
        // Never register a global union of tools: each route gets its own catalog.
        options.ConfigureSessionOptions = (http, server, cancellationToken) =>
        {
            var context = routeCatalogs.ForPath(http.Request.Path.Value!);

            server.ServerInfo = new Implementation { Name = context.Name, Version = "4.0.0" };
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
}

bool IsLoopbackOnlyRequest(HttpContext http)
{
    return http.Connection.RemoteIpAddress is { } address
           && IPAddress.IsLoopback(address)
           && (http.Request.Host.Host == "127.0.0.1" || http.Request.Host.Host == "localhost");
}

bool IsAllowedOrigin(HttpContext http, int hostPort)
{
    if (!http.Request.Headers.TryGetValue("Origin", out var origin))
    {
        return true;
    }

    return origin == $"http://127.0.0.1:{hostPort}" || origin == $"http://localhost:{hostPort}";
}

bool IsPreviewRequest(HttpContext http)
{
    return HttpMethods.IsGet(http.Request.Method) && http.Request.Path == PreviewPath;
}

bool HasDemoToken(HttpContext http)
{
    return http.Request.Headers.Authorization == DemoToken;
}
