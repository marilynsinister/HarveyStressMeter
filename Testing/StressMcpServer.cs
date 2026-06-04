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
            Tool("stress_debug_dump", "Structured diagnostic dump: Game, Player, Harvey, StressLoad, Debuffs, Treatments, Quests, Topics, GotoroRescue, SummaryFlags + legacy StressDialogueState."),
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
            Tool("mcp_set_time", "Set Game1.timeOfDay (Stardew format 600–2600, 10-minute steps).",
                Prop("time", "integer", "Stardew time, e.g. 900 for 9:00 AM")),
            Tool("mcp_add_minutes", "Advance game time by N minutes (10-minute Stardew steps).",
                Prop("minutes", "integer", "Minutes to add (>= 0)")),
            Tool("mcp_set_weather", "Set current weather flags (sun|rain|storm|snow|wind).",
                Prop("weather", "string", "sun, rain, storm, snow, or wind")),
            Tool("mcp_warp", "Warp player to a location with optional tile.",
                Prop("location", "string", "Location name, e.g. Forest, Hospital, FarmHouse"),
                Prop("x", "integer", "Tile X (optional; uses safe default if omitted)"),
                Prop("y", "integer", "Tile Y (optional; uses safe default if omitted)")),
            Tool("mcp_wait_seconds", "Real-time wait without blocking the game thread (tick-based).",
                Prop("seconds", "integer", "Seconds to wait (>= 0)")),
            Tool("mcp_set_friendship", "Set NPC friendship points and optional relationship status.",
                Prop("npc", "string", "NPC name, e.g. Harvey"),
                Prop("points", "integer", "Friendship points 0–2500"),
                Prop("relationship", "string", "Optional: none, Dating, or Married")),
            Tool("mcp_set_relationship", "Set NPC relationship status without changing friendship points.",
                Prop("npc", "string", "NPC name, e.g. Harvey"),
                Prop("relationship", "string", "none, Dating, or Married")),
            Tool("mcp_get_friendship", "Read-only friendship/relationship snapshot for an NPC.",
                Prop("npc", "string", "NPC name, e.g. Harvey")),
            Tool("mcp_place_npc", "Move an existing NPC to a location tile (no duplicate).",
                Prop("npc", "string", "NPC name, e.g. Harvey"),
                Prop("location", "string", "Location name, e.g. Hospital"),
                Prop("x", "integer", "Tile X"),
                Prop("y", "integer", "Tile Y")),
            Tool("mcp_add_topic", "Add conversation topic to player activeDialogueEvents.",
                Prop("topic", "string", "Topic id"),
                Prop("days", "integer", "Days remaining (optional, default 1; 0 = permanent)")),
            Tool("mcp_remove_topic", "Remove conversation topic from player.",
                Prop("topic", "string", "Topic id")),
            Tool("mcp_has_topic", "Read-only: check if topic exists and days left.",
                Prop("topic", "string", "Topic id")),
            Tool("mcp_list_topics", "List active conversation topics with optional filter.",
                Prop("filter", "string", "Optional substring filter, e.g. topicStress")),
            Tool("stress_trust_debug", "Read-only HarveyCareTrust snapshot (points, levels, caps, bonuses)."),
            Tool("stress_trust_set", "Debug: set HarveyCareTrust points (effective level capped by relationship).",
                Prop("points", "integer", "Trust points 0–MaxHarveyCareTrustPoints")),
            Tool("stress_trust_add", "Debug: add HarveyCareTrust points.",
                Prop("points", "integer", "Points to add (> 0)"),
                Prop("reason", "string", "Optional reason label for logs")),
            Tool("stress_trust_remove", "Debug: remove HarveyCareTrust points (bypasses penalty cooldown).",
                Prop("points", "integer", "Points to remove (> 0)"),
                Prop("reason", "string", "Optional reason label for logs")),
            Tool("stress_trust_reset", "Debug: reset HarveyCareTrust state to defaults."),
            Tool("stress_load_debug", "Read-only StressLoad snapshot (causes, offset, severity, bonuses, thresholds)."),
            Tool("stress_set_load", "Debug: set CurrentStressLoad via recovery offset (causes unchanged).",
                Prop("value", "integer", "Target CurrentStressLoad (0–MaxStressLoad)")),
            Tool("stress_apply_recovery", "Debug: increase StressRecoveryOffset (causes stay active).",
                Prop("amount", "integer", "Recovery amount (> 0)"),
                Prop("reason", "string", "Optional reason label")),
            Tool("stress_clear_recovery_offset", "Debug: reset StressRecoveryOffset to 0 and recalculate."),
            Tool("stress_gotoro_set_active", "Debug: toggle Gotoro flashback stress cause.",
                Prop("active", "boolean", "Enable or disable Gotoro flashback"),
                Prop("sync_topics", "boolean", "Sync topicStressGotoroFlashbackActive (default true)")),
            Tool("stress_force_recalculate", "Debug: force Recalculate() from current buffs/causes."),
            Tool("stress_rescue_debug", "Read-only Gotoro forest rescue evaluation snapshot."),
            Tool("stress_rescue_evaluate", "Evaluate rescue conditions; optional random roll without starting event.",
                Prop("ignore_chance", "boolean", "If true, skip random roll (default false)")),
            Tool("stress_rescue_force", "Debug: prepare rescue topics/state for CP (does not start event).",
                Prop("tier", "string", "auto, MidTrust, HighTrust, Dating, or Married"),
                Prop("force", "boolean", "If true, set topics and gotoro context (default true)")),
            Tool("stress_rescue_clear", "Remove topicStressGotoroForestRescuePending only."),
            Tool("stress_safe_aura_status", "Read-only Safe Person Aura snapshot (proximity, trust, recovery)."),
            Tool("stress_safe_aura_force_tick", "Debug: run one safe aura tick if context is safe (no event/menu/dialogue)."),
            Tool("stress_hud_snapshot", "Read-only stress meter HUD snapshot (visibility, load, causes, episodes)."),
            Tool("stress_treatment_snapshot", "Read-only stress treatment snapshot (debuffs, quests, topics, treatments)."),
            Tool("stress_treatment_debug",
                "Read-only treatment + StressLoad ActiveCauses + episode review flags (broader than stress_treatment_snapshot)."),
            Tool("stress_darkness_debug",
                "Read-only darkness therapy snapshot (FearLevel, step progress, buffs, quests, topics)."),
            Tool("stress_darkness_set_level", "Debug: set FearLevel 1–3, sync level buff and Harvey topics.",
                Prop("level", "integer", "Fear level: 1, 2, or 3")),
            Tool("stress_darkness_start_therapy", "Debug: DarknessService.StartTherapy + HarveyMod_DarknessStep1 quest."),
            Tool("stress_darkness_step1_progress",
                "Debug: set step1 progress (evenings completed + today units).",
                Prop("evenings", "integer", "Completed evenings (0–3)"),
                Prop("today", "integer", "Today's progress units (0–5)"),
                Prop("value", "integer", "Legacy: SafeDarknessMinutes (use with legacy=true)"),
                Prop("legacy", "boolean", "If true, write SafeDarknessMinutes instead of evenings/today")),
            Tool("stress_darkness_sync", "Debug: DarknessService.SyncDarknessState + before/after snapshot."),
            Tool("stress_social_get", "Read-only SocialExposureToday snapshot (status, thresholds, recovery timers)."),
            Tool("stress_social_set", "Debug: set SocialExposureToday (0–100).",
                Prop("value", "integer", "Target exposure 0–100")),
            Tool("stress_social_add", "Debug: add to SocialExposureToday.",
                Prop("amount", "integer", "Amount to add (can be negative)")),
            Tool("stress_social_reset", "Debug: reset SocialExposureToday and daily threshold HUD flags."),
            Tool("mcp_event_snapshot", "Read-only CP/vanilla event context snapshot."),
            Tool("mcp_start_event", "Debug: warp (optional) and start CP event by id.",
                Prop("event_id", "string", "Event id, e.g. HarveyStress_GotoroForestRescue_HighTrust"),
                Prop("location", "string", "Optional location to warp before start, e.g. Forest"),
                Prop("force", "boolean", "If true, ignore menu/dialogue block (default false)")),
            Tool("mcp_end_event", "Debug: safely skip/end current event if possible.",
                Prop("force", "boolean", "If true, ignore non-event menu/dialogue block (default false)")),
            Tool("mcp_event_advance", "Debug: advance current event script by N steps.",
                Prop("steps", "integer", "Steps to advance (default 1, max 50)"),
                Prop("force", "boolean", "If true, ignore non-event menu/dialogue block (default false)")),
            Tool("stress_force_start", "Debug: call TreatmentService.StartTreatment for an active debuff (production path).",
                Prop("buff_id", "string", "Buff ID, e.g. buffStressTired")),
            Tool("stress_episode_start", "Debug: start TreatmentEpisode by id (Burnout, PhysicalExhaustion, …).",
                Prop("episode_id", "string", "Episode id from StressEpisodes constants")),
            Tool("mcp_eat_item", "Simulate eating food for Hunger/TooCold quest progress.",
                Prop("item_category", "string", "Optional: food, hot_drink, coffee (default food)"),
                Prop("item_id", "string", "Optional qualified id, e.g. (O)221")),
            Tool("mcp_sleep", "Simulate bedtime: set time and run day-end sleep quest checks.",
                Prop("time", "integer", "Stardew time 600–2200 for early sleep (default 2200)")),
            Tool("mcp_save_game", "Write mod SaveData to slot CustomData (persistence tests)."),
            Tool("mcp_reload_save", "Reload mod SaveData from slot (simulates SaveLoaded path)."),
            Tool("stress_run_test_plan",
                "Run a predefined atomic test plan (smoke, all_debuffs, stressload, recovery, dialogues, …) and return a step report.",
                Prop("plan", "string",
                    "Plan id: smoke, all_debuffs, stressload, recovery, dialogues, quests, episodes, trust, gotoro_rescue, safe_aura, persistence, daily_regression"),
                Prop("stop_on_fail", "boolean", "Stop after first FAIL step (default true)")),
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
