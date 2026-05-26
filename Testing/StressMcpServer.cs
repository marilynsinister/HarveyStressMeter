using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StardewModdingAPI;

namespace HarveyStressMeter.Testing
{
    /// <summary>
    /// JSON-RPC MCP over HTTP for HarveyStressMeter debug/test commands (Cursor / agents).
    /// Protocol compatible with StardewMCP (initialize, tools/list, tools/call).
    /// </summary>
    public sealed class StressMcpServer : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IMonitor _monitor;
        private readonly Func<string, JsonElement?, string> _executeTool;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        public StressMcpServer(IMonitor monitor, Func<string, JsonElement?, string> executeTool)
        {
            _monitor = monitor;
            _executeTool = executeTool;
        }

        public void Start(int port)
        {
            if (_listener != null)
                return;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                _monitor.Log(
                    $"[StressMCP] Не удалось запустить HTTP на порту {port}: {ex.Message}. " +
                    "Проверьте, что порт свободен, или измените StressMcpPort в config.json.",
                    LogLevel.Error);
                _listener = null;
                return;
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            _monitor.Log($"[StressMCP] listening on http://localhost:{port}", LogLevel.Info);
        }

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // ignore shutdown races
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }

                _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                string body = await reader.ReadToEndAsync();

                string responseJson = string.IsNullOrWhiteSpace(body)
                    ? BuildError(null, -32700, "Parse error")
                    : HandleRequest(body);

                byte[] bytes = Encoding.UTF8.GetBytes(responseJson);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentEncoding = Encoding.UTF8;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                _monitor.Log($"[StressMCP] Request error: {ex}", LogLevel.Warn);
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch
                {
                    // ignore
                }
            }
        }

        private string HandleRequest(string body)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return BuildError(null, -32700, "Parse error");
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return BuildError(null, -32600, "Invalid Request");

                string? method = root.TryGetProperty("method", out JsonElement methodEl) && methodEl.ValueKind == JsonValueKind.String
                    ? methodEl.GetString()
                    : null;

                if (string.IsNullOrEmpty(method))
                    return BuildError(GetId(root), -32600, "Invalid Request");

                if (method == "notifications/initialized")
                    return string.Empty;

                JsonElement? id = root.TryGetProperty("id", out JsonElement idEl) ? idEl : null;

                return method switch
                {
                    "initialize" => BuildResult(id, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "HarveyStressMeter", version = "1.0.0" },
                    }),
                    "tools/list" => BuildResult(id, new { tools = StressMcpTools.All }),
                    "tools/call" => HandleToolsCall(root, id),
                    _ => BuildError(id, -32601, $"Method not found: {method}"),
                };
            }
        }

        private string HandleToolsCall(JsonElement root, JsonElement? id)
        {
            if (!root.TryGetProperty("params", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Object)
                return BuildError(id, -32602, "Invalid params");

            if (!parameters.TryGetProperty("name", out JsonElement nameEl) || nameEl.ValueKind != JsonValueKind.String)
                return BuildError(id, -32602, "Tool name required");

            string toolName = nameEl.GetString() ?? string.Empty;
            JsonElement? arguments = parameters.TryGetProperty("arguments", out JsonElement argsEl)
                ? argsEl
                : null;

            try
            {
                string text = _executeTool(toolName, arguments);
                return BuildResult(id, new
                {
                    content = new[]
                    {
                        new { type = "text", text },
                    },
                });
            }
            catch (Exception ex)
            {
                _monitor.Log($"[StressMCP] Tool {toolName} failed: {ex}", LogLevel.Error);
                return BuildResult(id, new
                {
                    content = new[]
                    {
                        new { type = "text", text = $"Error: {ex.Message}" },
                    },
                    isError = true,
                });
            }
        }

        private static JsonElement? GetId(JsonElement root) =>
            root.TryGetProperty("id", out JsonElement idEl) ? idEl : null;

        private static string BuildResult(JsonElement? id, object result)
        {
            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["result"] = result,
            };
            if (id.HasValue)
                payload["id"] = id.Value;

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static string BuildError(JsonElement? id, int code, string message)
        {
            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new { code, message },
            };
            if (id.HasValue)
                payload["id"] = id.Value;

            return JsonSerializer.Serialize(payload, JsonOptions);
        }
    }

    internal static class StressMcpTools
    {
        public static readonly object[] All =
        {
            Tool("stress_reset", "Full mod reset: buffs, quests, topics, save data (hs.reset)."),
            Tool("stress_debug_dump", "Full diagnostic: hs.debug + stress_dialogue_state."),
            Tool("stress_dialogue_state", "Read-only stress dialogue pipeline snapshot."),
            Tool("stress_game_context", "Read-only game context: event, menu, dialogue question/responses."),
            Tool("stress_add_debuff", "Apply stress debuff via production ApplyStressBuff (no treatment start).",
                Prop("buff_id", "string", "Buff ID, e.g. buffStressTired")),
            Tool("stress_remove_debuff", "Remove stress debuff, topics and active treatment.",
                Prop("buff_id", "string", "Buff ID")),
            Tool("stress_talk_harvey", "Warp to Harvey (optional) and open dialogue via checkAction.",
                Prop("no_warp", "boolean", "If true, do not warp (default false)")),
            Tool("stress_show_dialogue", "Open programmatic stress start dialogue directly (bypasses vanilla talk)."),
            Tool("stress_consent", "Close or advance open stress start dialogue (legacy consent command).",
                Prop("choice", "string", "accept or decline"),
                Prop("no_finish", "boolean", "If true, do not click through to close dialogue")),
            Tool("stress_list_responses", "List #$y quick responses in the open DialogueBox."),
            Tool("stress_choose_response", "Pick any dialogue response by key or index.",
                Prop("response_key", "string", "e.g. quickResponse1 (optional if index set)"),
                Prop("index", "integer", "0-based response index (optional if response_key set)"),
                Prop("no_advance", "boolean", "If true, do not auto-advance to question first"),
                Prop("no_finish", "boolean", "If true, do not click through after choice")),
            Tool("stress_dialogue_advance", "Click through DialogueBox.",
                Prop("steps", "integer", "Number of advances (default 1, max 20)")),
            Tool("stress_close_dialogue", "Force-close active menu (ESC). Clears pending auto-start treatment."),
        };

        private static object Tool(string name, string description, params object[] extraProperties)
        {
            var properties = new Dictionary<string, object>();
            foreach (object prop in extraProperties)
            {
                if (prop is ValueTuple<string, string, string> tuple)
                    properties[tuple.Item1] = new { type = tuple.Item2, description = tuple.Item3 };
            }

            return new
            {
                name,
                description,
                inputSchema = new
                {
                    type = "object",
                    properties,
                },
            };
        }

        private static (string, string, string) Prop(string name, string type, string description) =>
            (name, type, description);
    }
}
