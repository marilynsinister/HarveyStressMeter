using System;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Компактный Stress Meter HUD + опциональный debug overlay.
    /// TODO: заменить прямоугольники на assets/stress_meter.png, когда текстура будет готова.
    /// </summary>
    public sealed class StressMeterHudService
    {
        private const int PulseDurationTicks = 90;
        private const int MessageCooldownTicks = 3600;

        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        private readonly ModConfig _config;
        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly TreatmentEpisodeService _episodeService;
        private readonly StateService _stateService;

        private Texture2D? _meterTexture;
        private bool _debugOverlayVisible;
        private int _lastTrackedLoad = -1;
        private StressSeverity _lastTrackedSeverity = StressSeverity.Calm;
        private int _pulseTicks;
        private int _messageCooldownTicks;
        private int _lastMessageThreshold;

        public StressMeterHudService(
            IModHelper helper,
            IMonitor monitor,
            ModConfig config,
            SaveData data,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            StateService stateService)
        {
            _helper = helper;
            _monitor = monitor;
            _config = config;
            _data = data;
            _stressLoadService = stressLoadService;
            _episodeService = episodeService;
            _stateService = stateService;
        }

        public bool DebugOverlayVisible => _debugOverlayVisible || _config.ShowDebugOverlay;

        public void Subscribe()
        {
            _helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            _helper.Events.Display.RenderedHud += OnRenderedHud;
            _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            _helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            RegisterConsoleCommands();
        }

        public void ToggleDebugOverlay()
        {
            _debugOverlayVisible = !_debugOverlayVisible;
            _monitor.Log($"[StressHud] Debug overlay: {(_debugOverlayVisible ? "ON" : "OFF")}", LogLevel.Info);
        }

        public void SetDebugOverlay(bool visible) => _debugOverlayVisible = visible;

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            try
            {
                _meterTexture = _helper.ModContent.Load<Texture2D>("assets/stress_meter.png");
            }
            catch (Exception)
            {
                _meterTexture = null;
                _monitor.Log("[StressHud] assets/stress_meter.png not found — using vector bar fallback", LogLevel.Trace);
            }
        }

        private void RegisterConsoleCommands()
        {
            _helper.ConsoleCommands.Add(
                "hs.stress_hud",
                "Stress meter HUD: toggle overlay | overlay on/off | messages on/off",
                OnConsoleCommand);
        }

        private void OnConsoleCommand(string command, string[] args)
        {
            if (args.Length == 0)
            {
                _monitor.Log(
                    "Usage: hs.stress_hud toggle | overlay on|off | messages on|off\n" +
                    "Config: ShowStressMeter=false disables meter entirely.",
                    LogLevel.Info);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "toggle":
                    ToggleDebugOverlay();
                    break;
                case "overlay":
                    SetDebugOverlay(ParseOnOff(args, 1, defaultOn: true));
                    break;
                case "messages":
                    _config.EnableHudMessages = ParseOnOff(args, 1, defaultOn: true);
                    _helper.WriteConfig(_config);
                    _monitor.Log($"[StressHud] EnableHudMessages={_config.EnableHudMessages}", LogLevel.Info);
                    break;
                default:
                    _monitor.Log("Unknown subcommand. Use toggle | overlay | messages", LogLevel.Warn);
                    break;
            }
        }

        private static bool ParseOnOff(string[] args, int index, bool defaultOn)
        {
            if (args.Length <= index)
                return defaultOn;

            return args[index].ToLowerInvariant() switch
            {
                "on" or "1" or "true" => true,
                "off" or "0" or "false" => false,
                _ => defaultOn,
            };
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (_config.StressMeterDebugToggle.JustPressed())
            {
                ToggleDebugOverlay();
                _helper.Input.SuppressActiveKeybinds(_config.StressMeterDebugToggle);
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            var load = _stressLoadService.GetCurrentStressLoad();
            var severity = _stressLoadService.GetSeverity();

            if (load != _lastTrackedLoad || severity != _lastTrackedSeverity)
            {
                if (Math.Abs(load - _lastTrackedLoad) >= 5 || severity != _lastTrackedSeverity)
                    _pulseTicks = PulseDurationTicks;

                TryShowChangeMessage(load, severity);
                _lastTrackedLoad = load;
                _lastTrackedSeverity = severity;
            }

            if (_pulseTicks > 0)
                _pulseTicks--;

            if (_messageCooldownTicks > 0)
                _messageCooldownTicks--;
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!ShouldDrawAnyHud())
                return;

            var debugMode = IsDebugMode();
            if (ShouldDrawCompactMeter(debugMode))
                DrawCompactMeter(e.SpriteBatch, debugMode);

            if (ShouldDrawDebugOverlay(debugMode))
                DrawDebugOverlay(e.SpriteBatch);
        }

        private bool ShouldDrawAnyHud()
        {
            if (!_config.ShowStressMeter && !DebugOverlayVisible && !_config.ShowDebugOverlay)
                return false;

            if (!Context.IsWorldReady)
                return false;

            if (GameStateHelper.IsEventActive())
                return false;

            if (IsFestivalUiContext())
                return false;

            if (Game1.activeClickableMenu != null && !DebugOverlayVisible && !_config.ShowDebugOverlay)
                return false;

            return true;
        }

        private bool ShouldDrawCompactMeter(bool debugMode)
        {
            if (!_config.ShowStressMeter)
                return false;

            return ShouldShowMeterByStressRules(debugMode);
        }

        private bool ShouldDrawDebugOverlay(bool debugMode) =>
            DebugOverlayVisible || (_config.ShowDebugOverlay && debugMode);

        private bool ShouldShowMeterByStressRules(bool debugMode)
        {
            var load = _stressLoadService.GetCurrentStressLoad();

            if (debugMode)
                return true;

            if (!string.IsNullOrEmpty(_stressLoadService.GetActiveTreatmentEpisodeId()))
                return true;

            if (!string.IsNullOrEmpty(_stressLoadService.GetActiveEpisodeId()))
                return true;

            if (load >= _config.MildThreshold)
                return true;

            if (!_config.ShowOnlyWhenStressed && load > 0)
                return true;

            return false;
        }

        private bool IsDebugMode() =>
            DebugOverlayVisible || _config.ShowDebugOverlay || _config.ShowDebugNumbers;

        private void DrawCompactMeter(SpriteBatch spriteBatch, bool debugMode)
        {
            var load = _stressLoadService.GetCurrentStressLoad();
            var severity = _stressLoadService.GetSeverity();
            var label = GetSeverityLabel(severity);
            var colors = GetSeverityColors(severity);

            float pulse = 1f;
            if (_pulseTicks > 0)
                pulse = 0.75f + 0.25f * MathF.Sin(_pulseTicks * 0.25f);

            int barWidth = (int)(128 * _config.Scale);
            int barHeight = Math.Max(6, (int)(8 * _config.Scale));
            int padding = Math.Max(4, (int)(4 * _config.Scale));
            var origin = ResolveAnchorOrigin(barWidth, barHeight + padding + Game1.smallFont.LineSpacing);

            float opacity = Math.Clamp(_config.Opacity, 0.1f, 1f) * pulse;

            var backRect = new Rectangle(origin.X, origin.Y, barWidth, barHeight);
            DrawFilledRect(spriteBatch, backRect, colors.Background * opacity);

            int fillWidth = (int)(barWidth * (load / (float)_stressLoadService.GetMaxStressLoad()));
            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(origin.X, origin.Y, fillWidth, barHeight);
                DrawFilledRect(spriteBatch, fillRect, colors.Fill * opacity);
            }

            if (severity >= StressSeverity.Critical && _pulseTicks > 0)
            {
                var glowRect = new Rectangle(origin.X - 1, origin.Y - 1, barWidth + 2, barHeight + 2);
                DrawFilledRect(spriteBatch, glowRect, colors.Fill * (0.25f * pulse));
            }

            var text = _config.ShowDebugNumbers || debugMode
                ? $"{label} {load}"
                : label;

            var textSize = Game1.smallFont.MeasureString(text);
            var textPos = new Vector2(
                origin.X + (barWidth - textSize.X) / 2f,
                origin.Y + barHeight + 2);

            spriteBatch.DrawString(
                Game1.smallFont,
                text,
                textPos + new Vector2(1, 1),
                Color.Black * (opacity * 0.7f));

            spriteBatch.DrawString(
                Game1.smallFont,
                text,
                textPos,
                colors.Text * opacity);
        }

        private void DrawDebugOverlay(SpriteBatch spriteBatch)
        {
            var load = _stressLoadService.GetCurrentStressLoad();
            var raw = _stressLoadService.GetRawStressLoad();
            var severity = _stressLoadService.GetSeverity();
            var selection = _episodeService.EvaluateSelection();
            var primaryTreatment = StressDebuffSelector.GetPrimaryTreatmentAwaitingReview(_stateService)
                ?? _data.StressState.GetActiveTreatmentsWithQuests().FirstOrDefault();

            bool hasBuff = primaryTreatment != null
                && _stateService.HasBuffInGame(primaryTreatment.BuffId);

            var nextStep = TreatmentNextStep.Resolve(primaryTreatment, hasBuff);
            var causes = _stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .Select(kvp => kvp.Key)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Stress: {load} (raw {raw}) / {severity}");
            sb.AppendLine($"Causes: {(causes.Count > 0 ? string.Join(", ", causes) : "(none)")}");
            sb.AppendLine($"Candidate: {_stressLoadService.GetCandidateEpisode() ?? "(none)"}");
            sb.AppendLine($"Active episode: {_stressLoadService.GetActiveEpisodeId() ?? "(none)"}");
            sb.AppendLine($"Treatment episode: {_stressLoadService.GetActiveTreatmentEpisodeId() ?? "(none)"}");
            sb.AppendLine($"Awaiting review: {_stressLoadService.IsAwaitingHarveyReview()}");
            sb.AppendLine($"Selection: {selection.Action} ({selection.Reason ?? "-"})");
            sb.AppendLine($"Next: {nextStep}");
            sb.AppendLine("--- Flashback ---");
            sb.AppendLine($"Active: {_data.ThunderFlashback.IsActive}, gotoro: {_data.ThunderFlashback.IsGotoroFlashback}");
            sb.AppendLine(
                $"Shelter: {_data.ThunderFlashback.ForestShelterSeconds}/{_data.ThunderFlashback.RequiredForestShelterSeconds}");

            var text = sb.ToString().TrimEnd();
            var size = Game1.smallFont.MeasureString(text);
            int pad = 8;
            int boxW = (int)size.X + pad * 2;
            int boxH = (int)size.Y + pad * 2;
            var box = new Rectangle(12, 12, boxW, boxH);

            DrawFilledRect(spriteBatch, box, new Color(0, 0, 0, 160));
            spriteBatch.DrawString(Game1.smallFont, text, new Vector2(box.X + pad, box.Y + pad), Color.White);
        }

        private void DrawFilledRect(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            if (_meterTexture != null)
            {
                spriteBatch.Draw(_meterTexture, rect, color);
                return;
            }

            spriteBatch.Draw(Game1.staminaRect, rect, color);
        }

        private Point ResolveAnchorOrigin(int width, int totalHeight)
        {
            int marginX = _config.OffsetX;
            int marginY = _config.OffsetY;
            var viewport = Game1.uiViewport;

            return _config.Anchor switch
            {
                StressHudAnchor.BottomLeft => new Point(marginX, viewport.Height - totalHeight - marginY),
                StressHudAnchor.TopRight => new Point(viewport.Width - width - marginX, marginY),
                StressHudAnchor.TopLeft => new Point(marginX, marginY),
                _ => new Point(viewport.Width - width - marginX, viewport.Height - totalHeight - marginY),
            };
        }

        private void TryShowChangeMessage(int load, StressSeverity severity)
        {
            if (!_config.EnableHudMessages || _messageCooldownTicks > 0)
                return;

            if (GameStateHelper.IsEventActive() || Game1.activeClickableMenu != null)
                return;

            int threshold = load switch
            {
                var l when l >= _config.CriticalThreshold => _config.CriticalThreshold,
                var l when l >= _config.HighThreshold => _config.HighThreshold,
                var l when l >= _config.MildThreshold => _config.MildThreshold,
                _ => 0,
            };

            bool severityChanged = severity != _lastTrackedSeverity && _lastTrackedLoad >= 0;
            bool crossedUp = threshold > 0 && threshold > _lastMessageThreshold && load >= threshold;

            if (!severityChanged && !crossedUp)
                return;

            string? message = severity switch
            {
                StressSeverity.Critical => "Критический уровень стресса…",
                StressSeverity.High => "Стресс высокий.",
                StressSeverity.Mild when crossedUp || severityChanged => "Напряжение нарастает…",
                _ => null,
            };

            if (message == null)
                return;

            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
            _messageCooldownTicks = MessageCooldownTicks;
            _lastMessageThreshold = threshold;
        }

        private static string GetSeverityLabel(StressSeverity severity) => severity switch
        {
            StressSeverity.Critical => "Критический стресс",
            StressSeverity.High => "Высокий стресс",
            StressSeverity.Mild => "Напряжение",
            _ => "Спокойно",
        };

        private static (Color Background, Color Fill, Color Text) GetSeverityColors(StressSeverity severity) =>
            severity switch
            {
                StressSeverity.Critical => (new Color(48, 16, 16), new Color(200, 48, 48), new Color(255, 180, 180)),
                StressSeverity.High => (new Color(48, 32, 8), new Color(230, 120, 32), new Color(255, 220, 170)),
                StressSeverity.Mild => (new Color(40, 40, 16), new Color(210, 190, 64), new Color(255, 245, 200)),
                _ => (new Color(24, 32, 24), new Color(96, 140, 96), new Color(210, 230, 210)),
            };

        private static bool IsFestivalUiContext()
        {
            try
            {
                if (Game1.isFestival())
                    return true;
            }
            catch
            {
                // ignored — fallback below
            }

            var locationName = Game1.currentLocation?.NameOrUniqueName ?? "";
            return locationName.Contains("Festival", StringComparison.OrdinalIgnoreCase);
        }
    }
}
