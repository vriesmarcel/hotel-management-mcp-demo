using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Project_HMS.Mcp;

static class Program
{
    static int checks;
    static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks++; }
    static int Main(string[] args)
    {
        string oldConnection = Environment.GetEnvironmentVariable("HMS_CONNECTION_STRING");
        try
        {
            if (args.Length != 1) throw new InvalidOperationException("Pass the local connection configuration path.");
            var xml = XDocument.Load(args[0]);
            Environment.SetEnvironmentVariable("HMS_CONNECTION_STRING", xml.Root.Elements("add").Single(x => (string)x.Attribute("name") == "HotelDatabase").Attribute("connectionString").Value);
            Run().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + checks + " no-refactoring MCP assertions; all three protocol versions; real SQL reads only.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e.GetType().Name + " (details suppressed to protect local configuration)"); return 1; }
        finally { Environment.SetEnvironmentVariable("HMS_CONNECTION_STRING", oldConnection); }
    }
    static async Task Run()
    {
        Check(!typeof(AvailabilityTools).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Hotel.")), "No extracted Hotel service assemblies.");
        var adapter = new AvailabilityTools();
        foreach (var invalid in new[] { "2030-1-10", "2030-02-30", "2030-01-10T00:00:00Z", " 2030-01-10", null })
        {
            bool rejected = false;
            try { adapter.GetAvailableRooms(invalid, "2030-01-13"); } catch (McpException) { rejected = true; }
            Check(rejected, "Invalid date rejected before opening SQL.");
        }
        foreach (var end in new[] { "2030-01-10", "2030-01-09", "2031-01-11" })
        {
            bool rejected = false;
            try { adapter.GetAvailableRooms("2030-01-10", end); } catch (McpException) { rejected = true; }
            Check(rejected, "Invalid duration rejected.");
        }
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var tool = McpServerTool.Create(typeof(AvailabilityTools).GetMethod("GetAvailableRooms"), adapter);
        using var host = new EmbeddedMcpHost(tool, port: port); host.Start();
        foreach (var version in new[] { EmbeddedMcpHost.ModernVersion, EmbeddedMcpHost.LegacyVersion, EmbeddedMcpHost.CodexVersion })
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = host.Endpoint, TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string,string> { ["Authorization"] = "Bearer 123456" }
            });
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = version });
            var tools = await client.ListToolsAsync();
            Check(tools.Count == 1 && tools[0].Name == "GetAvailableRooms", "One available tool.");
            var resources = await client.ListResourcesAsync();
            Check(resources.Any(r => r.Uri == "hotel://room-types") && resources.Any(r => r.Uri == "hotel://policies/cancellation/v1"), "Reference resources discoverable.");
            var templates = await client.ListResourceTemplatesAsync();
            Check(templates.Any(r => r.UriTemplate == "hotel://room-types/{category}"), "Room category template discoverable.");
            var family = await client.ReadResourceAsync("hotel://room-types/Family");
            Check(family.Contents.OfType<ModelContextProtocol.Protocol.TextResourceContents>().Any(r => r.Text.Contains("\"maximumGuests\":5")), "Family capacity read through MCP.");
            var policy = await client.ReadResourceAsync("hotel://policies/cancellation/v1");
            Check(policy.Contents.OfType<ModelContextProtocol.Protocol.TextResourceContents>().Any(r => r.Text.Contains("Europe/Berlin") && r.Text.Contains("48")), "Cancellation policy readable.");
            var result = await client.CallToolAsync("GetAvailableRooms", new Dictionary<string,object> { ["checkIn"]="2026-11-18", ["checkOut"]="2026-11-21" });
            Check(result.IsError != true && result.StructuredContent.HasValue, "Live read returned structured content.");
            Check(result.StructuredContent.Value.GetProperty("nights").GetInt32() == 3, "Three nights.");
            Check(result.StructuredContent.Value.GetProperty("rooms").ValueKind == System.Text.Json.JsonValueKind.Array, "Typed rooms.");
            var invalid = await client.CallToolAsync("GetAvailableRooms", new Dictionary<string,object> { ["checkIn"]="2026-11-18", ["checkOut"]="2026-11-18" });
            Check(invalid.IsError == true, "Validation error travels through MCP.");
        }
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, host.Endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer wrong");
        using var response = await http.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "Wrong token still rejected.");
    }
}
