using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>MCP/SMAPI machine-readable darkness therapy snapshot.</summary>
    public static class DarknessDebugReporter
    {
        public const string DevPrefix = TreatmentDebugReporter.DevPrefix;

        private static readonly string[] DarknessBuffIds =
        {
            BuffIds.Darkness,
            BuffIds.DarknessLevel1,
            BuffIds.DarknessLevel2,
            BuffIds.DarknessLevel3,
            BuffIds.DimLight,
            BuffIds.HarveyLantern,
            BuffIds.DarknessOvercome,
        };

        private static readonly string[] DarknessQuestIds =
        {
            QuestIds.Darkness,
            QuestIds.DarknessStep1,
            QuestIds.DarknessStep2,
            QuestIds.DarknessStep3,
        };

        private static readonly string[] DarknessTopicIds =
        {
            "topicStressDarknessLevel2",
            "topicStressDarknessLevel3",
            "topicDarknessTherapyStart",
            "topicDarknessStep1Complete",
            "topicDarknessStep2Complete",
            "topicDarknessFullyCured",
            TopicIds.StressDarkness,
        };

        public static string BuildMcpSnapshot(SaveData data, StateService stateService, QuestService? questService = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine("=== darkness ===");

            if (data?.Darkness == null)
            {
                sb.AppendLine("error: Darkness save state is null");
                return sb.ToString().TrimEnd();
            }

            var d = data.Darkness;
            sb.AppendLine($"FearLevel: {d.FearLevel}");
            sb.AppendLine($"IsTherapyActive: {d.IsTherapyActive}");
            sb.AppendLine($"TherapyStage: {d.TherapyStage}");
            sb.AppendLine($"IsCured: {d.IsCured}");
            sb.AppendLine($"HasOvercomeBonus: {d.HasOvercomeBonus}");
            sb.AppendLine($"SafeDarknessMinutes: {d.SafeDarknessMinutes}");
            sb.AppendLine(
                $"SafeDarknessEveningsCompleted: {d.SafeDarknessEveningsCompleted}/{DarknessLegacyHelper.Step1EveningsRequired}");
            sb.AppendLine(
                $"SafeDarknessProgressToday: {d.SafeDarknessProgressToday}/{DarknessLegacyHelper.Step1MinutesPerEvening}");
            sb.AppendLine($"LastSafeDarknessDate: {d.LastSafeDarknessDate?.ToString() ?? "(null)"}");
            sb.AppendLine(
                $"SafeZonesVisited: {(d.SafeZonesVisited?.Count > 0 ? string.Join(", ", d.SafeZonesVisited) : "(none)")}");
            sb.AppendLine($"MountainNightSeconds: {d.MountainNightSeconds}");

            sb.AppendLine("=== Darkness Step1 ===");
            sb.AppendLine($"  TherapyStage: {d.TherapyStage}");
            sb.AppendLine($"  IsTherapyActive: {d.IsTherapyActive}");
            sb.AppendLine(
                $"  SafeDarknessProgressToday: {d.SafeDarknessProgressToday}/{DarknessLegacyHelper.Step1MinutesPerEvening}");
            sb.AppendLine(
                $"  SafeDarknessEveningsCompleted: {d.SafeDarknessEveningsCompleted}/{DarknessLegacyHelper.Step1EveningsRequired}");
            sb.AppendLine($"  LastSafeDarknessDate: {d.LastSafeDarknessDate?.ToString() ?? "(null)"}");
            sb.AppendLine(
                $"  {QuestIds.DarknessStep1}: {FormatQuestActive(questService, QuestIds.DarknessStep1)}");
            sb.AppendLine(
                $"  {QuestIds.DarknessStep1} currentObjective: {GetStep1QuestObjective(questService) ?? "(n/a)"}");
            sb.AppendLine($"  {BuffIds.DimLight}: {FormatBuffActive(stateService, BuffIds.DimLight)}");

            sb.AppendLine("DarknessBuffs:");
            foreach (var buffId in DarknessBuffIds)
                sb.AppendLine($"  {buffId}: {FormatBuffActive(stateService, buffId)}");

            sb.AppendLine("DarknessQuests:");
            foreach (var questId in DarknessQuestIds)
                sb.AppendLine($"  {questId}: {FormatQuestActive(questService, questId)}");

            sb.AppendLine("DarknessTopics:");
            foreach (var topicId in DarknessTopicIds)
                sb.AppendLine($"  {topicId}: {FormatTopicActive(topicId)}");

            if (d.IsTherapyActive && d.TherapyStage >= 1)
            {
                var stepQuest = DarknessLegacyHelper.GetStepQuestIdForStage(d.TherapyStage);
                sb.AppendLine($"CurrentStepQuest: {stepQuest ?? "(none)"}");
                sb.AppendLine($"CurrentObjective: {DarknessLegacyHelper.GetStepQuestCurrentObjective(d.TherapyStage) ?? "(n/a)"}");
            }

            DarknessLegacyHelper.AppendDarknessProgressSnapshot(sb, d);
            return sb.ToString().TrimEnd();
        }

        public static void LogSnapshot(IMonitor monitor, string header, string snapshot)
        {
            monitor.Log($"{DevPrefix} === {header} ===", LogLevel.Info);
            foreach (var line in snapshot.Split('\n'))
                monitor.Log($"{DevPrefix} {line}", LogLevel.Info);
        }

        private static string FormatBuffActive(StateService? stateService, string buffId)
        {
            if (stateService == null || !Context.IsWorldReady)
                return "n/a";

            return stateService.HasBuffInGame(buffId) ? "active" : "inactive";
        }

        private static string FormatQuestActive(QuestService? questService, string questId)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return "n/a";

            if (questService != null)
                return questService.HasQuest(questId) ? "inJournal" : "absent";

            return Game1.player.hasQuest(questId) ? "inJournal" : "absent";
        }

        private static string FormatTopicActive(string topicId)
        {
            if (!Context.IsWorldReady)
                return "n/a";

            return ConversationHelper.HasTopic(topicId) ? "active" : "inactive";
        }

        private static string? GetStep1QuestObjective(QuestService? questService)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return null;

            if (questService != null && !questService.HasQuest(QuestIds.DarknessStep1))
                return null;

            if (questService == null && !Game1.player.hasQuest(QuestIds.DarknessStep1))
                return null;

            var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == QuestIds.DarknessStep1);
            return quest?.currentObjective?.Replace('\n', ' ');
        }
    }
}
