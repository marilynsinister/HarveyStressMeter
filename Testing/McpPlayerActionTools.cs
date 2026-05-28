using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewValley;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP tools simulating player eat/sleep for quest progress (EnableStressMcp only).</summary>
    internal static class McpPlayerActionTools
    {
        private static readonly Dictionary<string, string> DefaultFoodByCategory = new(StringComparer.OrdinalIgnoreCase)
        {
            ["food"] = "(O)221",
            ["hot_drink"] = "(O)395",
            ["coffee"] = "(O)395",
        };

        public static string EatItem(
            GameLogicHandler gameLogicHandler,
            StateService stateService,
            JsonElement? arguments)
        {
            if (McpEnvironmentTools.EnvironmentBlockMessage("mcp_eat_item") is { } blocked)
                return blocked;

            var itemId = ResolveFoodItemId(arguments, out var resolveError);
            if (itemId == null)
                return resolveError ?? "Error: could not resolve food item.";

            var item = ItemRegistry.Create(itemId);
            if (item is not StardewValley.Object food)
                return $"Error: item '{itemId}' is not edible Object.";

            if (food.Edibility <= 0)
                return $"Error: item '{itemId}' has Edibility <= 0.";

            var hungerBefore = stateService.GetActiveTreatment(BuffIds.Hunger);
            var wasAwaitingReview = hungerBefore?.AwaitingHarveyReview ?? false;
            var questActive = stateService.HasActiveQuestState(QuestIds.Hunger);

            gameLogicHandler.OnFoodConsumed(food);

            var hungerAfter = stateService.GetActiveTreatment(BuffIds.Hunger);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"itemId: {itemId}");
            sb.AppendLine($"displayName: {food.DisplayName ?? food.Name}");
            sb.AppendLine($"HungerQuestActive: {questActive}");
            sb.AppendLine($"AteAnyFood: {hungerAfter?.Progress?.AteAnyFood ?? hungerBefore?.Progress?.AteAnyFood ?? false}");
            sb.AppendLine($"AwaitingHarveyReview: {hungerAfter?.AwaitingHarveyReview ?? wasAwaitingReview}");
            sb.AppendLine($"hasTopicAteToday: {ConversationHelper.HasTopic(TopicIds.AteToday)}");
            return sb.ToString().TrimEnd();
        }

        public static string Sleep(
            GameLogicHandler gameLogicHandler,
            StateService stateService,
            SaveData data,
            JsonElement? arguments)
        {
            if (McpEnvironmentTools.EnvironmentBlockMessage("mcp_sleep") is { } blocked)
                return blocked;

            var time = 2200;
            if (TryGetInt(arguments, "time", out var requestedTime))
                time = requestedTime;

            if (!McpEnvironmentTools.IsValidStardewTime(time))
                return $"Error: invalid Stardew time '{time}'. Use 600–2600 with 10-minute steps.";

            var oldTime = Game1.timeOfDay;
            Game1.timeOfDay = time;

            var noSleepBefore = stateService.GetActiveTreatment(BuffIds.NoSleep);
            var wasAwaitingReview = noSleepBefore?.AwaitingHarveyReview ?? false;

            gameLogicHandler.CheckDayEndingQuestCompletion();
            gameLogicHandler.CheckLateSleepPattern();

            var noSleepAfter = stateService.GetActiveTreatment(BuffIds.NoSleep);
            var earlySleep = time >= 600 && time <= 2200;

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"oldTime: {oldTime}");
            sb.AppendLine($"newTime: {Game1.timeOfDay}");
            sb.AppendLine($"earlySleepWindow: {earlySleep}");
            sb.AppendLine($"NoSleepQuestActive: {stateService.HasActiveQuestState(QuestIds.NoSleep)}");
            sb.AppendLine($"AwaitingHarveyReview: {noSleepAfter?.AwaitingHarveyReview ?? wasAwaitingReview}");
            sb.AppendLine($"DaysWithLateSleep: {data.DaysWithLateSleep}");
            sb.AppendLine("warning: simulates bedtime checks only; does not advance the calendar day.");
            return sb.ToString().TrimEnd();
        }

        private static string? ResolveFoodItemId(JsonElement? arguments, out string? error)
        {
            error = null;

            if (TryGetString(arguments, "item_id", out var itemId))
            {
                if (!itemId.StartsWith("(O)", StringComparison.Ordinal))
                    itemId = $"(O){itemId.Trim()}";
                return itemId;
            }

            var category = "food";
            if (TryGetString(arguments, "item_category", out var categoryRaw))
                category = categoryRaw.Trim();

            if (DefaultFoodByCategory.TryGetValue(category, out var defaultId))
                return defaultId;

            error = $"Error: unknown item_category '{category}'. Use food, hot_drink, coffee, or item_id.";
            return null;
        }

        private static bool TryGetString(JsonElement? arguments, string name, out string value)
        {
            value = string.Empty;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            return el.TryGetInt32(out value);
        }
    }
}
