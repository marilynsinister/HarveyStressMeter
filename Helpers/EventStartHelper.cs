using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    public static class EventStartHelper
    {
        public static string? FindEventKey(IReadOnlyDictionary<string, string> events, string eventId)
        {
            foreach (var key in events.Keys)
            {
                if (string.Equals(key, eventId, StringComparison.OrdinalIgnoreCase))
                    return key;

                if (key.StartsWith(eventId + "/", StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            return null;
        }

        public static bool HasCpEventDefinition(GameLocation location, string eventId)
        {
            var events = TryLoadLocationEvents(location);
            return events != null && FindEventKey(events, eventId) != null;
        }

        private static IReadOnlyDictionary<string, string>? TryLoadLocationEvents(GameLocation location)
        {
            var locationName = location.NameOrUniqueName ?? location.Name;
            if (string.IsNullOrEmpty(locationName))
                return null;

            try
            {
                return Game1.content.Load<Dictionary<string, string>>($"Data\\Events\\{locationName}");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Запускает rescue: CP script из Data/Events (farmer -1 -1), иначе dynamic fallback.
        /// </summary>
        public static bool TryStartRescueEvent(
            GameLocation location,
            string eventId,
            string tier,
            IMonitor monitor,
            out bool usedCpEvent)
        {
            usedCpEvent = false;

            if (GameStateHelper.IsEventActive())
                return false;

            var events = TryLoadLocationEvents(location);
            if (events != null)
            {
                var key = FindEventKey(events, eventId);
                if (key != null && events.TryGetValue(key, out var cpScript) && !string.IsNullOrWhiteSpace(cpScript))
                {
                    try
                    {
                        var evt = new Event(cpScript);
                        evt.id = eventId;
                        location.startEvent(evt);
                        usedCpEvent = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        monitor.Log(
                            $"[FlashbackRescue] CP event '{eventId}' failed: {ex.Message}; trying dynamic fallback.",
                            LogLevel.Warn);
                    }
                }
            }

            try
            {
                var dynamicScript = RescueEventScriptBuilder.Build(tier, eventId);
                var dynamicEvent = new Event(dynamicScript);
                dynamicEvent.id = eventId;
                location.startEvent(dynamicEvent);
                monitor.Log(
                    $"[FlashbackRescue] No CP script for '{eventId}' in {location.NameOrUniqueName}; used dynamic fallback.",
                    LogLevel.Warn);
                return true;
            }
            catch (Exception ex)
            {
                monitor.Log(
                    $"[FlashbackRescue] Dynamic event '{eventId}' failed: {ex.Message}",
                    LogLevel.Error);
                return false;
            }
        }
    }
}
