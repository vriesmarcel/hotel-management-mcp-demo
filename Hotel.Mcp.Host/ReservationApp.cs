using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Hotel.Mcp.Host;

internal static class ReservationApp
{
    public const string ResourceUri = "ui://hotel/reservations/v1";
    private static readonly Lazy<string> AppHtml = new(() => ReadEmbedded("reservations"));
    private static readonly Lazy<string> PreviewHtml = new(() => ReadEmbedded("preview"));

    public static string Preview() => PreviewHtml.Value;

    public static McpServerResource CreateResource() => McpServerResource.Create(
        (Func<ReadResourceResult>)Read,
        new McpServerResourceCreateOptions
        {
            Name = "ReservationApp", UriTemplate = ResourceUri, MimeType = McpApps.HtmlMimeType,
            Description = "Interactive room selection, package choices, server-priced quotes and confirmed reservations."
        });

    public static ReadResourceResult Read() => new()
    {
        Contents = [new TextResourceContents
        {
            Uri = ResourceUri, MimeType = McpApps.HtmlMimeType, Text = AppHtml.Value,
            // This app needs no network access, external assets or browser permissions.
            Meta = new JsonObject { ["ui"] = new JsonObject
            {
                ["prefersBorder"] = true,
                ["csp"] = new JsonObject { ["connectDomains"] = new JsonArray(), ["resourceDomains"] = new JsonArray() }
            } }
        }]
    };

    private static string ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Hotel.Mcp.Host.Ui.dist.{name}.html")
            ?? throw new InvalidOperationException("Build the MCP Apps UI with npm ci and npm run build in Hotel.Mcp.Host/Ui.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
