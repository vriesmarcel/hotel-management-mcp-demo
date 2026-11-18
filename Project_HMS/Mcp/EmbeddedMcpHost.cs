using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Project_HMS.Mcp
{
    // The SDK handles MCP. This adapter supplies the Framework HTTP envelope and lifetime.
    // Local presentation only. The public demo token is not production authentication.
    public sealed class EmbeddedMcpHost : IDisposable
    {
        public const int DefaultPort = 5057;
        public const string DemoToken = "123456";
        public const string ModernVersion = "2026-07-28";
        public const string LegacyVersion = "2025-11-25";
        // Codex desktop currently initializes with this Streamable HTTP revision.
        public const string CodexVersion = "2025-06-18";
        private const int MaxBodyBytes = 65536;
        private readonly HttpListener listener = new HttpListener();
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private readonly ConcurrentDictionary<int, Task> requests = new ConcurrentDictionary<int, Task>();
        private readonly SemaphoreSlim slots = new SemaphoreSlim(8);
        private readonly McpServerTool tool;
        private readonly byte[] tokenHash;
        private Task acceptLoop;
        private int nextId;
        private int disposed;
        public Uri Endpoint { get; }
        public event Action<string> Activity;

        public EmbeddedMcpHost(McpServerTool tool, string token = DemoToken, int port = DefaultPort)
        {
            this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
            if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
                throw new ArgumentException("The demo bearer token must not be empty or contain whitespace.");
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            using (var sha = SHA256.Create()) tokenHash = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            Endpoint = new Uri("http://127.0.0.1:" + port + "/mcp/");
            listener.Prefixes.Add(Endpoint.AbsoluteUri);
        }

        public void Start()
        {
            if (acceptLoop != null) throw new InvalidOperationException("MCP host has already started.");
            listener.Start(); // Fail synchronously, so the UI can report port/URL ACL errors.
            acceptLoop = Task.Run(AcceptAsync);
            Report("MCP listening at " + Endpoint + " inside PID " + System.Diagnostics.Process.GetCurrentProcess().Id);
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(false);
                    if (!slots.Wait(0)) { context.Response.StatusCode = 503; context.Response.Close(); continue; }
                    int id = Interlocked.Increment(ref nextId);
                    var task = Task.Run(async () =>
                    {
                        try { await HandleAsync(context).ConfigureAwait(false); }
                        finally { slots.Release(); }
                    });
                    requests[id] = task;
                    _ = task.ContinueWith(completed => requests.TryRemove(id, out _), TaskScheduler.Default);
                }
            }
            catch (HttpListenerException) when (stopping.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(System.Diagnostics.Debugger.IsAttached ? 300 : 30));
            // Closing aborts both slow request reads and response writes on Framework.
            using var abort = timeout.Token.Register(() => { try { response.Abort(); } catch (ObjectDisposedException) { } });
            try
            {
                ApplyNoCacheHeader(response);
                if (!TryValidateSecurityBoundary(request, response)) return;
                if (!TryValidateEndpoint(request, response)) return;
                if (!TryAuthorizeRequest(request, response)) return;
                if (!TryValidateHttpMethod(request, response)) return;
                if (!TryValidateContentType(request, response)) return;
                if (!TryValidateAcceptHeader(request, response)) return;
                var bytes = await ReadBoundedAsync(request.InputStream, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                if (!IsValidJsonRpcEnvelope(root))
                {
                    await Error(response, null, -32600, "Invalid JSON-RPC request.", 400);
                    return;
                }

                if (!TryGetRequestId(root, out var id))
                {
                    await Error(response, null, -32600, "Invalid request ID.", 400);
                    return;
                }

                string method = GetString(root, "method");
                var parameters = root.TryGetProperty("params", out var parsed) ? parsed : default;
                string version = ResolveProtocolVersion(request, method, parameters);

                if (!IsSupportedProtocolVersion(version))
                {
                    await Error(response, id, -32022, "Unsupported protocol version.", 400,
                        new { requested = version, supported = new[] { ModernVersion, LegacyVersion, CodexVersion } });
                    return;
                }

                if (!await TryValidateProtocolConsistencyAsync(request, response, method, parameters, version, id).ConfigureAwait(false))
                    return;

                var message = JsonSerializer.Deserialize<JsonRpcMessage>(bytes, McpJsonUtilities.DefaultOptions);
                message.Context = new JsonRpcMessageContext { ProtocolVersion = version };
                var transport = new StreamableHttpServerTransport { Stateless = true };
                await using var server = McpServer.Create(transport, new McpServerOptions
                {
                    ProtocolVersion = version,
                    ServerInfo = new Implementation { Name = "hotel-reservations-net48", Version = "0.2.0" },
                    ServerInstructions = "Use hotel-local calendar dates. Read hotel://room-types and hotel://policies/cancellation/v1 for hotel facts. Availability also includes this reference data for clients without resource access. Availability does not reserve a room. Currency is not recorded in the legacy database.",
                    Capabilities = new ServerCapabilities { Tools = new ToolsCapability(), Resources = new ResourcesCapability() },
                    ResourceCollection = Hotel.Mcp.ReferenceResources.Create(),
                    ToolCollection = new McpServerPrimitiveCollection<McpServerTool>
                    {
                        tool
                    }
                });
                using var running = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                var run = server.RunAsync(running.Token);
                try
                {
                    response.ContentType = "text/event-stream";
                    response.ContentEncoding = Encoding.UTF8;
                    response.SendChunked = true;
                    var wrote = await transport.HandlePostRequestAsync(message, response.OutputStream, first =>
                    {
                        if (first is JsonRpcError error)
                            response.StatusCode = GetStreamResponseStatusCode(error.Error.Code);
                        return default;
                    }, timeout.Token).ConfigureAwait(false);
                    if (!wrote) response.StatusCode = 202;
                    Report("MCP " + method + " completed (" + version + ").");
                }
                finally
                {
                    running.Cancel();
                    try { await run.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
            }
            catch (BodyTooLargeException) { response.StatusCode = 413; }
            catch (JsonException) { await Error(response, null, -32700, "Invalid JSON.", 400).ConfigureAwait(false); }
            catch (OperationCanceledException) { Report("MCP request cancelled or timed out."); }
            catch (Exception exception)
            {
                // Never log exception messages: transport and SQL errors may contain secrets.
                Report("MCP request failed: " + exception.GetType().Name);
                try { response.StatusCode = 500; } catch (Exception) { }
            }
            finally { try { response.Close(); } catch (ObjectDisposedException) { } }
        }

        private void ApplyNoCacheHeader(HttpListenerResponse response)
        {
            response.Headers["Cache-Control"] = "no-store";
        }

        private bool TryValidateSecurityBoundary(HttpListenerRequest request, HttpListenerResponse response)
        {
            bool isLoopback = request.RemoteEndPoint != null && IPAddress.IsLoopback(request.RemoteEndPoint.Address);
            bool hostMatches = string.Equals(request.Headers["Host"], Endpoint.Authority, StringComparison.OrdinalIgnoreCase);
            bool originMatches = request.Headers["Origin"] == null || request.Headers["Origin"] == Endpoint.GetLeftPart(UriPartial.Authority);
            if (isLoopback && hostMatches && originMatches) return true;
            response.StatusCode = 403;
            return false;
        }

        private bool TryValidateEndpoint(HttpListenerRequest request, HttpListenerResponse response)
        {
            bool pathMatches = request.Url.AbsolutePath == Endpoint.AbsolutePath;
            bool hasNoQuery = request.Url.Query.Length == 0;
            if (pathMatches && hasNoQuery) return true;
            response.StatusCode = 404;
            return false;
        }

        private bool TryAuthorizeRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (Authorized(request.Headers["Authorization"])) return true;
            response.StatusCode = 401;
            // Framework requires AddHeader for this restricted response header.
            response.AddHeader("WWW-Authenticate", "Bearer realm=\"hotel-local-demo\"");
            return false;
        }

        private static bool TryValidateHttpMethod(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.HttpMethod == "POST") return true;
            response.StatusCode = 405;
            response.Headers["Allow"] = "POST";
            return false;
        }

        private static bool TryValidateContentType(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) && contentType.MediaType == "application/json")
                return true;
            response.StatusCode = 415;
            return false;
        }

        private static bool TryValidateAcceptHeader(HttpListenerRequest request, HttpListenerResponse response)
        {
            var accept = GetAcceptedMediaTypes(request.Headers["Accept"]);
            if (accept.Contains("application/json") && accept.Contains("text/event-stream")) return true;
            response.StatusCode = 406;
            return false;
        }

        private static string[] GetAcceptedMediaTypes(string acceptHeader)
        {
            return (acceptHeader ?? string.Empty)
                .Split(',')
                .Select(value => value.Split(';')[0].Trim())
                .ToArray();
        }

        private static bool IsValidJsonRpcEnvelope(JsonElement root)
        {
            return root.ValueKind == JsonValueKind.Object &&
                GetString(root, "jsonrpc") == "2.0" &&
                GetString(root, "method") != null;
        }

        private static bool TryGetRequestId(JsonElement root, out JsonElement? id)
        {
            id = root.TryGetProperty("id", out var requestId) ? requestId : (JsonElement?)null;
            if (!id.HasValue) return true;
            return id.Value.ValueKind == JsonValueKind.String ||
                (id.Value.ValueKind == JsonValueKind.Number && id.Value.TryGetInt64(out _));
        }

        private string ResolveProtocolVersion(HttpListenerRequest request, string method, JsonElement parameters)
        {
            string version = request.Headers["MCP-Protocol-Version"];
            // Only the legacy initialization exchange may omit the version header.
            if (version == null && method == "initialize") version = GetString(parameters, "protocolVersion");
            if (version == null && method == "notifications/initialized") version = LegacyVersion;
            return version;
        }

        private static bool IsSupportedProtocolVersion(string version)
        {
            return version == ModernVersion || version == LegacyVersion || version == CodexVersion;
        }

        private async Task<bool> TryValidateProtocolConsistencyAsync(
            HttpListenerRequest request,
            HttpListenerResponse response,
            string method,
            JsonElement parameters,
            string version,
            JsonElement? id)
        {
            if (version == ModernVersion)
            {
                var meta = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("_meta", out var m) ? m : default;
                bool methodMatches = request.Headers["Mcp-Method"] == method;
                bool versionMatches = GetString(meta, "io.modelcontextprotocol/protocolVersion") == version;
                bool nameMatches = !RequiresMcpNameHeaderMatch(method) ||
                    DecodeHeader(request.Headers["Mcp-Name"]) == (GetString(parameters, "name") ?? GetString(parameters, "uri"));
                if (methodMatches && versionMatches && nameMatches) return true;

                await Error(response, id, -32020, "Required MCP headers must match the request body.", 400);
                return false;
            }

            if (method != "initialize" || GetString(parameters, "protocolVersion") == version) return true;
            await Error(response, id, -32020, "Protocol header and initialize version differ.", 400);
            return false;
        }

        private static bool RequiresMcpNameHeaderMatch(string method)
        {
            return method == "tools/call" || method == "prompts/get" || method == "resources/read";
        }

        private static int GetStreamResponseStatusCode(int rpcErrorCode)
        {
            if (rpcErrorCode == -32601) return 404;
            if (rpcErrorCode == -32022 || rpcErrorCode == -32020 || rpcErrorCode == -32021) return 400;
            return 200;
        }

        private bool Authorized(string header)
        {
            if (header == null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length > 4096) return false;
            byte[] actual;
            using (var sha = SHA256.Create()) actual = sha.ComputeHash(Encoding.UTF8.GetBytes(header.Substring(7)));
            int difference = 0;
            for (int i = 0; i < actual.Length; i++) difference |= actual[i] ^ tokenHash[i];
            return difference == 0;
        }

        private static string GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static string DecodeHeader(string value)
        {
            if (value == null || !value.StartsWith("=?base64?", StringComparison.Ordinal) || !value.EndsWith("?=", StringComparison.Ordinal)) return value;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(9, value.Length - 11))); }
            catch (FormatException) { return null; }
        }
        private static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken ct)
        {
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > MaxBodyBytes) throw new BodyTooLargeException();
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        private static async Task Error(HttpListenerResponse response, JsonElement? id, int code, string message, int status, object data = null)
        {
            response.StatusCode = status;
            response.ContentType = "application/json";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, error = new { code, message, data } });
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }
        private void Report(string value) { try { Activity?.Invoke(value); } catch (Exception) { } }
        private sealed class BodyTooLargeException : Exception { }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stopping.Cancel();
            listener.Close();
            if (acceptLoop != null) acceptLoop.GetAwaiter().GetResult();
            Task.WhenAll(requests.Values.ToArray()).GetAwaiter().GetResult();
            slots.Dispose();
            stopping.Dispose();
        }
    }
}
