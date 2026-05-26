using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace HarveyStressMeter.Helpers
{
    public sealed class DialogueResponseOption
    {
        public int Index { get; init; }
        public string ResponseKey { get; init; } = "";
        public string ResponseText { get; init; } = "";
    }

    public sealed class DialogueBoxSnapshot
    {
        public bool HasDialogueBox { get; init; }
        public bool IsQuestion { get; init; }
        public IReadOnlyList<DialogueResponseOption> Responses { get; init; } = Array.Empty<DialogueResponseOption>();
    }

    /// <summary>
    /// DEV/TEST: программный разговор с Harvey, advance и выбор #$y без клика мышью.
    /// </summary>
    internal static class HarveyDevTalkHelper
    {
        private const int MaxDialogueAdvances = 24;
        internal const int MaxDialogueAdvancesPublic = MaxDialogueAdvances;

        public static bool TryTalkToHarvey(IMonitor monitor, bool warpIfNeeded = true)
        {
            if (GameStateHelper.IsEventActive())
            {
                monitor.Log("[DEV/TEST] talk-harvey blocked: event active", LogLevel.Warn);
                return false;
            }

            if (Game1.activeClickableMenu != null)
            {
                monitor.Log(
                    $"[DEV/TEST] talk-harvey blocked: menu open ({Game1.activeClickableMenu.GetType().Name})",
                    LogLevel.Warn);
                return false;
            }

            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey?.currentLocation == null)
            {
                monitor.Log("[DEV/TEST] talk-harvey blocked: Harvey not found", LogLevel.Warn);
                return false;
            }

            var harveyLocation = harvey.currentLocation;

            if (Game1.currentLocation != harveyLocation)
            {
                if (!warpIfNeeded)
                {
                    monitor.Log(
                        $"[DEV/TEST] talk-harvey blocked: player in {Game1.currentLocation?.Name}, Harvey in {harveyLocation.Name}",
                        LogLevel.Warn);
                    return false;
                }

                var talkTile = GetTalkTile(harveyLocation, harvey);
                var warpName = harveyLocation.NameOrUniqueName ?? harveyLocation.Name;
                Game1.warpFarmer(warpName, (int)talkTile.X, (int)talkTile.Y, 2);
                monitor.Log(
                    $"[DEV/TEST] talk-harvey: warped to {warpName} ({(int)talkTile.X},{(int)talkTile.Y})",
                    LogLevel.Info);

                if (GameStateHelper.IsEventActive())
                {
                    monitor.Log(
                        $"[DEV/TEST] talk-harvey blocked: event started after warp (EventId={GameStateHelper.GetCurrentEventId() ?? "unknown"})",
                        LogLevel.Warn);
                    return false;
                }
            }

            Game1.player.faceGeneralDirection(harvey.getStandingPosition());
            harvey.faceGeneralDirection(Game1.player.getStandingPosition());

            if (!harvey.checkAction(Game1.player, harveyLocation))
            {
                monitor.Log("[DEV/TEST] talk-harvey: checkAction returned false", LogLevel.Warn);
                return false;
            }

            monitor.Log("[DEV/TEST] talk-harvey: dialogue opened via checkAction", LogLevel.Info);
            return true;
        }

        public static DialogueBoxSnapshot GetDialogueBoxSnapshot()
        {
            if (Game1.activeClickableMenu is not DialogueBox box)
            {
                return new DialogueBoxSnapshot
                {
                    HasDialogueBox = false,
                    IsQuestion = false,
                };
            }

            var responses = box.responses?
                .Select((r, i) => new DialogueResponseOption
                {
                    Index = i,
                    ResponseKey = r.responseKey ?? "",
                    ResponseText = r.responseText ?? "",
                })
                .ToList() ?? new List<DialogueResponseOption>();

            return new DialogueBoxSnapshot
            {
                HasDialogueBox = true,
                IsQuestion = box.isQuestion && responses.Count > 0,
                Responses = responses,
            };
        }

        public static string FormatResponsesForLog(DialogueBoxSnapshot snapshot)
        {
            if (snapshot.Responses.Count == 0)
                return "(none)";

            return string.Join("; ", snapshot.Responses.Select(r => $"[{r.Index}] {r.ResponseKey}"));
        }

        /// <summary>Legacy dev helper: consent #$y removed — closes or advances the open stress start dialogue.</summary>
        public static bool TryChooseConsent(IMonitor monitor, bool accept, bool finishDialogue = true)
        {
            monitor.Log(
                $"[DEV/TEST] consent flow removed — {(finishDialogue ? "closing" : "advancing")} stress start dialogue (legacy accept/decline={accept})",
                LogLevel.Info);

            if (finishDialogue)
            {
                FinishOpenDialogue(monitor);
                return Game1.activeClickableMenu is not DialogueBox;
            }

            return AdvanceDialogue(monitor, 1) > 0;
        }

        public static bool TryChooseResponse(
            IMonitor monitor,
            string? responseKey = null,
            int? responseIndex = null,
            bool advanceToQuestion = true,
            bool finishDialogue = false)
        {
            if (GameStateHelper.IsEventActive())
            {
                monitor.Log(
                    $"[DEV/TEST] choose-response blocked: event active (EventId={GameStateHelper.GetCurrentEventId() ?? "unknown"})",
                    LogLevel.Warn);
                return false;
            }

            if (Game1.activeClickableMenu is not DialogueBox box)
            {
                monitor.Log("[DEV/TEST] choose-response blocked: no DialogueBox open", LogLevel.Warn);
                return false;
            }

            if (advanceToQuestion && !AdvanceUntilQuestion(monitor, ref box))
                return false;

            if (!box.isQuestion || box.responses == null || box.responses.Length == 0)
            {
                monitor.Log("[DEV/TEST] choose-response blocked: not at a question", LogLevel.Warn);
                return false;
            }

            Response? response = null;
            if (!string.IsNullOrWhiteSpace(responseKey))
            {
                response = box.responses.FirstOrDefault(r => r.responseKey == responseKey);
                if (response == null)
                {
                    monitor.Log(
                        $"[DEV/TEST] choose-response blocked: key '{responseKey}' not found (available: {FormatResponsesForLog(GetDialogueBoxSnapshot())})",
                        LogLevel.Warn);
                    return false;
                }
            }
            else if (responseIndex.HasValue)
            {
                if (responseIndex.Value < 0 || responseIndex.Value >= box.responses.Length)
                {
                    monitor.Log(
                        $"[DEV/TEST] choose-response blocked: index {responseIndex.Value} out of range (count={box.responses.Length})",
                        LogLevel.Warn);
                    return false;
                }

                response = box.responses[responseIndex.Value];
            }
            else
            {
                monitor.Log("[DEV/TEST] choose-response blocked: response_key or index required", LogLevel.Warn);
                return false;
            }

            var dialogue = Game1.currentSpeaker?.CurrentDialogue?.Peek();
            if (dialogue == null)
            {
                monitor.Log("[DEV/TEST] choose-response blocked: no active Dialogue on speaker", LogLevel.Warn);
                return false;
            }

            if (!dialogue.chooseResponse(response))
            {
                monitor.Log("[DEV/TEST] choose-response: chooseResponse returned false", LogLevel.Warn);
                return false;
            }

            monitor.Log(
                $"[DEV/TEST] choose-response: chose {response.responseKey} ({response.responseText})",
                LogLevel.Info);

            if (finishDialogue)
                FinishOpenDialogue(monitor);

            return true;
        }

        /// <summary>Закрывает любое активное меню (в т.ч. DialogueBox на #$y) — для MCP cleanup.</summary>
        public static bool TryForceCloseDialogue(IMonitor monitor)
        {
            if (Game1.activeClickableMenu == null)
                return false;

            var menuName = Game1.activeClickableMenu.GetType().Name;
            Game1.exitActiveMenu();
            monitor.Log($"[DEV/TEST] force-closed menu: {menuName}", LogLevel.Info);
            return true;
        }

        public static int AdvanceDialogue(IMonitor monitor, int steps = 1)
        {
            if (GameStateHelper.IsEventActive())
            {
                monitor.Log(
                    $"[DEV/TEST] dialogue-advance blocked: event active (EventId={GameStateHelper.GetCurrentEventId() ?? "unknown"})",
                    LogLevel.Warn);
                return 0;
            }

            var advanced = 0;

            for (var i = 0; i < steps; i++)
            {
                if (Game1.activeClickableMenu is not DialogueBox box)
                    break;

                if (box.isQuestion && box.responses?.Length > 0)
                {
                    monitor.Log("[DEV/TEST] dialogue-advance: stopped at question (use hs.test.choose-response)", LogLevel.Info);
                    break;
                }

                if (!TryClickDialogueBox(box))
                    break;

                advanced++;
            }

            if (advanced > 0)
                monitor.Log($"[DEV/TEST] dialogue-advance: {advanced} step(s)", LogLevel.Info);

            return advanced;
        }

        private static bool AdvanceUntilQuestion(IMonitor monitor, ref DialogueBox box)
        {
            for (var i = 0; i < MaxDialogueAdvances; i++)
            {
                if (box.isQuestion && box.responses?.Length > 0)
                    return true;

                if (!TryClickDialogueBox(box))
                    return false;

                if (Game1.activeClickableMenu is not DialogueBox next)
                {
                    monitor.Log("[DEV/TEST] consent: dialogue closed before question appeared", LogLevel.Warn);
                    return false;
                }

                box = next;
            }

            monitor.Log("[DEV/TEST] consent: question not reached (advance limit)", LogLevel.Warn);
            return false;
        }

        private static void FinishOpenDialogue(IMonitor monitor)
        {
            for (var i = 0; i < MaxDialogueAdvances; i++)
            {
                if (Game1.activeClickableMenu is not DialogueBox box)
                {
                    monitor.Log("[DEV/TEST] consent: dialogue closed", LogLevel.Info);
                    return;
                }

                if (box.isQuestion && box.responses?.Length > 0)
                    return;

                if (!TryClickDialogueBox(box))
                    return;
            }

            monitor.Log("[DEV/TEST] consent: dialogue still open after advance limit", LogLevel.Warn);
        }

        private static bool TryClickDialogueBox(DialogueBox box)
        {
            var x = box.x + Math.Max(1, box.width / 2);
            var y = box.y + Math.Max(1, box.height / 2);
            box.receiveLeftClick(x, y);
            return true;
        }

        private static Vector2 GetTalkTile(GameLocation location, NPC harvey)
        {
            var origin = harvey.Tile;
            var candidates = new[]
            {
                origin + new Vector2(0, 1),
                origin + new Vector2(0, -1),
                origin + new Vector2(1, 0),
                origin + new Vector2(-1, 0),
                origin,
            };

            foreach (var tile in candidates)
            {
                if (location.isTileOnMap((int)tile.X, (int)tile.Y)
                    && location.isTilePassable(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport))
                {
                    return tile;
                }
            }

            return origin + new Vector2(0, 1);
        }
    }
}
