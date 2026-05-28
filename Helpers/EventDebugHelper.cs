using System;
using System.Reflection;
using System.Text;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Constants;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Defensive CP event debug helpers for MCP / DEV tests.</summary>
    public static class EventDebugHelper
    {
        private const int MaxAdvanceSteps = 50;

        public static string BuildMcpSnapshot()
        {
            var evt = GetActiveEvent();
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"eventActive: {GameStateHelper.IsEventActive()}");
            sb.AppendLine($"eventUp: {Game1.eventUp}");
            sb.AppendLine($"eventId: {GameStateHelper.GetCurrentEventId() ?? "(none)"}");
            sb.AppendLine($"eventName: {GetEventName(evt) ?? "(none)"}");
            sb.AppendLine($"location: {Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? "null"}");
            sb.AppendLine($"currentCommandIndex: {GetCurrentCommandIndex(evt)}");
            sb.AppendLine($"eventCommandCount: {GetEventCommandCount(evt)}");
            sb.AppendLine($"isFestival: {IsFestivalDay()}");
            sb.AppendLine($"currentMenu: {Game1.activeClickableMenu?.GetType().Name ?? "null"}");
            sb.AppendLine($"dialogueBoxActive: {Game1.dialogueUp || Game1.activeClickableMenu is DialogueBox}");
            sb.AppendLine($"playerCanMove: {GetPlayerCanMove()}");
            return sb.ToString().TrimEnd();
        }

        public static EventStartDebugResult TryStartEvent(
            string eventId,
            IMonitor monitor)
        {
            var result = new EventStartDebugResult
            {
                BeforeSnapshot = BuildMcpSnapshot(),
            };

            if (string.IsNullOrWhiteSpace(eventId))
            {
                result.UnsupportedReason = "event_id_required";
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            eventId = eventId.Trim();

            var location = Game1.currentLocation;
            if (location == null)
            {
                result.UnsupportedReason = "no_current_location";
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            bool usedCp;
            if (eventId.StartsWith("HarveyStress_GotoroForestRescue_", StringComparison.Ordinal))
            {
                if (TryStartRescueEventIfApplicable(location, eventId, monitor, out usedCp, out var rescueError))
                {
                    result.Started = true;
                    result.UsedCpScript = usedCp;
                }
                else
                {
                    result.UnsupportedReason = rescueError ?? "rescue_event_start_failed";
                }
            }
            else if (EventStartHelper.TryStartCpEvent(location, eventId, monitor, out var cpError, out usedCp))
            {
                result.Started = true;
                result.UsedCpScript = usedCp;
            }
            else
            {
                result.UnsupportedReason = cpError ?? "start_failed";
            }

            result.AfterSnapshot = BuildMcpSnapshot();
            return result;
        }

        public static string? GetStartBlockReason(bool force)
        {
            if (GameStateHelper.IsEventActive())
                return "event_already_active";

            return GetNonEventUiBlockReason(force);
        }

        public static EventEndDebugResult TryEndEvent(bool force)
        {
            var result = new EventEndDebugResult
            {
                BeforeSnapshot = BuildMcpSnapshot(),
            };

            if (GetNonEventUiBlockReason(force) is { } uiBlock)
            {
                result.UnsupportedReason = uiBlock;
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            if (force)
                GameStateHelper.ForceCloseActiveMenu();

            var evt = GetActiveEvent();
            if (evt != null)
            {
                try
                {
                    evt.skipEvent();
                }
                catch (Exception ex)
                {
                    if (!TryInvokeEventEndFallback(evt, out var fallbackError))
                        result.UnsupportedReason = fallbackError ?? ex.Message;
                }
            }

            ForceClearActiveEventState(force);
            result.Ended = !GameStateHelper.IsEventActive();
            if (!result.Ended && result.UnsupportedReason == null)
                result.UnsupportedReason = "event_still_active_after_force_end";

            result.AfterSnapshot = BuildMcpSnapshot();
            return result;
        }

        public static EventAdvanceDebugResult TryAdvanceEvent(int steps, bool force)
        {
            var result = new EventAdvanceDebugResult
            {
                BeforeSnapshot = BuildMcpSnapshot(),
                RequestedSteps = Math.Clamp(steps, 1, MaxAdvanceSteps),
            };

            if (GetNonEventUiBlockReason(force) is { } uiBlock)
            {
                result.UnsupportedReason = uiBlock;
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            var evt = GetActiveEvent();
            if (evt == null)
            {
                result.UnsupportedReason = "no_active_event";
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            var checkMethod = typeof(Event).GetMethod(
                "checkForNextCommand",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (checkMethod == null)
            {
                result.UnsupportedReason = "checkForNextCommand_unavailable";
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            var location = Game1.currentLocation;
            if (location == null)
            {
                result.UnsupportedReason = "no_current_location";
                result.AfterSnapshot = BuildMcpSnapshot();
                return result;
            }

            for (var i = 0; i < result.RequestedSteps && GameStateHelper.IsEventActive(); i++)
            {
                if (force && Game1.activeClickableMenu is DialogueBox dialogueBox)
                {
                    dialogueBox.receiveLeftClick(
                        dialogueBox.x + Math.Max(1, dialogueBox.width / 2),
                        dialogueBox.y + Math.Max(1, dialogueBox.height / 2));
                    result.AdvancedSteps++;
                    continue;
                }

                var beforeIndex = GetCurrentCommandIndex(evt);
                try
                {
                    checkMethod.Invoke(evt, new object[] { location, Game1.timeOfDay });
                }
                catch (Exception ex)
                {
                    result.UnsupportedReason = ex.Message;
                    break;
                }

                var afterIndex = GetCurrentCommandIndex(evt);
                result.AdvancedSteps++;

                if (beforeIndex == afterIndex)
                {
                    result.StuckAtCommandIndex = afterIndex;
                    result.Warning = "event_command_index_did_not_advance; event may wait for input or animation";
                    break;
                }
            }

            result.Advanced = result.AdvancedSteps > 0;
            if (!result.Advanced && result.UnsupportedReason == null)
                result.UnsupportedReason = "advance_failed";

            result.AfterSnapshot = BuildMcpSnapshot();
            return result;
        }

        private static bool TryStartRescueEventIfApplicable(
            GameLocation location,
            string eventId,
            IMonitor monitor,
            out bool usedCp,
            out string? error)
        {
            usedCp = false;
            error = null;

            if (!eventId.StartsWith("HarveyStress_GotoroForestRescue_", StringComparison.Ordinal))
                return false;

            var tier = eventId switch
            {
                FlashbackRescueEventIds.MidTrust => FlashbackRescueTiers.MidTrust,
                FlashbackRescueEventIds.HighTrust => FlashbackRescueTiers.HighTrust,
                FlashbackRescueEventIds.Dating => FlashbackRescueTiers.Dating,
                FlashbackRescueEventIds.Married => FlashbackRescueTiers.Married,
                _ => null,
            };

            if (tier == null)
            {
                error = "unknown_rescue_event_id";
                return false;
            }

            if (EventStartHelper.TryStartRescueEvent(location, eventId, tier, monitor, out usedCp))
                return true;

            error = "rescue_event_start_failed";
            return false;
        }

        private static string? GetNonEventUiBlockReason(bool force)
        {
            if (force)
                return null;

            if (Game1.activeClickableMenu != null)
                return $"menu_active:{Game1.activeClickableMenu.GetType().Name}";

            if (Game1.dialogueUp)
                return "dialogue_active";

            return null;
        }

        private static bool TryInvokeEventEndFallback(Event evt, out string? error)
        {
            error = null;

            foreach (var methodName in new[] { "exitEvent", "endEvent", "forceEnd" })
            {
                var onEvent = typeof(Event).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (onEvent == null)
                    continue;

                try
                {
                    onEvent.Invoke(evt, null);
                    if (!GameStateHelper.IsEventActive())
                        return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            var location = Game1.currentLocation;
            if (location != null)
            {
                foreach (var methodName in new[] { "endEvent", "finishEvent" })
                {
                    var onLocation = typeof(GameLocation).GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (onLocation == null)
                        continue;

                    try
                    {
                        onLocation.Invoke(location, null);
                        if (!GameStateHelper.IsEventActive())
                            return true;
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }
            }

            return false;
        }

        private static void ForceClearActiveEventState(bool force)
        {
            if (force)
                GameStateHelper.ForceCloseActiveMenu();

            var location = Game1.currentLocation;
            if (location?.currentEvent != null)
                location.currentEvent = null;

            Game1.eventUp = false;
            GameStateHelper.ForceClearFadeAndWarpFlags();
            GameStateHelper.ClearStaleUiFlags();
        }

        private static Event? GetActiveEvent()
            => Game1.CurrentEvent ?? Game1.currentLocation?.currentEvent;

        private static string? GetEventName(Event? evt)
        {
            if (evt == null)
                return null;

            if (!string.IsNullOrWhiteSpace(evt.id))
                return evt.id;

            return null;
        }

        private static int GetCurrentCommandIndex(Event? evt)
        {
            if (evt == null)
                return -1;

            return evt.currentCommand;
        }

        private static int GetEventCommandCount(Event? evt)
            => evt?.eventCommands?.Length ?? -1;

        private static bool GetPlayerCanMove()
        {
            try
            {
                return Game1.player.canMove;
            }
            catch
            {
                return !Game1.eventUp && Game1.CurrentEvent == null;
            }
        }

        private static bool IsFestivalDay()
        {
            try
            {
                return Game1.isFestival();
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class EventStartDebugResult
    {
        public string BeforeSnapshot { get; set; } = "";
        public string AfterSnapshot { get; set; } = "";
        public bool Started { get; set; }
        public bool UsedCpScript { get; set; }
        public string? WarpedTo { get; set; }
        public string? UnsupportedReason { get; set; }
    }

    public sealed class EventEndDebugResult
    {
        public string BeforeSnapshot { get; set; } = "";
        public string AfterSnapshot { get; set; } = "";
        public bool Ended { get; set; }
        public string? UnsupportedReason { get; set; }
    }

    public sealed class EventAdvanceDebugResult
    {
        public string BeforeSnapshot { get; set; } = "";
        public string AfterSnapshot { get; set; } = "";
        public int RequestedSteps { get; set; }
        public int AdvancedSteps { get; set; }
        public bool Advanced { get; set; }
        public int StuckAtCommandIndex { get; set; } = -1;
        public string? UnsupportedReason { get; set; }
        public string? Warning { get; set; }
    }
}
