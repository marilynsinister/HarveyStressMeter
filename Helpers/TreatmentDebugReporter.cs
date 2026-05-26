using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Подробный debug-вывод состояния stress treatment (SMAPI log + HUD).
    /// </summary>
    public static class TreatmentDebugReporter
    {
        public const string DevPrefix = "[DEV/TEST]";

        public static void LogActiveTreatments(IMonitor monitor, SaveData data, StateService stateService)
        {
            monitor.Log($"{DevPrefix} === stress_debug_state: active treatments ===", LogLevel.Info);

            if (data.StressState.ActiveTreatments.Count == 0)
            {
                monitor.Log($"{DevPrefix} (no active treatments in save)", LogLevel.Info);
                LogUntreatedDebuffs(monitor, stateService);
                return;
            }

            foreach (var treatment in data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
                LogTreatmentLine(monitor, treatment, stateService);

            LogUntreatedDebuffs(monitor, stateService);
        }

        public static void LogTreatmentDetail(IMonitor monitor, StateService stateService, string buffId)
        {
            monitor.Log($"{DevPrefix} --- treatment detail: {buffId} ---", LogLevel.Info);

            var treatment = stateService.GetActiveTreatment(buffId);
            if (treatment == null)
            {
                monitor.Log($"{DevPrefix} no active treatment record for buffId={buffId}", LogLevel.Info);
                monitor.Log($"{DevPrefix} hasBuffInGame={stateService.HasBuffInGame(buffId)}", LogLevel.Info);
                monitor.Log(
                    $"{DevPrefix} next step: {TreatmentNextStep.Resolve(null, stateService.HasBuffInGame(buffId))}",
                    LogLevel.Info);
                return;
            }

            LogTreatmentLine(monitor, treatment, stateService);
        }

        public static string BuildActiveTreatmentsSummary(SaveData data, StateService stateService)
        {
            var sb = new StringBuilder();

            if (data.StressState.ActiveTreatments.Count == 0)
            {
                sb.AppendLine("(no active treatments)");
                return sb.ToString().TrimEnd();
            }

            foreach (var treatment in data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
                sb.AppendLine(FormatTreatmentLine(treatment, stateService));

            return sb.ToString().TrimEnd();
        }

        public static void ShowHud(string message)
        {
            if (!Context.IsWorldReady)
                return;

            Game1.addHUDMessage(new HUDMessage($"{DevPrefix} {message}", HUDMessage.newQuest_type));
        }

        private static void LogUntreatedDebuffs(IMonitor monitor, StateService stateService)
        {
            var untreated = StressDebuffSelector.GetUntreatedDebuffs(stateService);
            monitor.Log(
                $"{DevPrefix} untreated debuffs (no quest yet): {(untreated.Count > 0 ? string.Join(", ", untreated) : "(none)")}",
                LogLevel.Info);
        }

        private static void LogTreatmentLine(IMonitor monitor, TreatmentState treatment, StateService stateService)
        {
            foreach (var line in FormatTreatmentLine(treatment, stateService).Split('\n'))
                monitor.Log($"{DevPrefix} {line}", LogLevel.Info);
        }

        private static string FormatTreatmentLine(TreatmentState treatment, StateService stateService)
        {
            var hasBuff = stateService.HasBuffInGame(treatment.BuffId);
            var nextStep = TreatmentNextStep.Resolve(treatment, hasBuff);
            var reviewTopic = TreatmentTopics.GetReadyForReviewTopic(treatment.BuffId);
            var reviewTopicActive = reviewTopic != null && ConversationHelper.HasTopic(reviewTopic);

            return string.Join('\n',
            [
                $"[{treatment.TreatmentKey}]",
                $"  buffId={treatment.BuffId}",
                $"  questId={treatment.QuestId ?? "(empty)"}",
                $"  TreatmentStarted={treatment.TreatmentStarted}",
                $"  ObjectivesCompleted={treatment.ObjectivesCompleted}",
                $"  AwaitingHarveyReview={treatment.AwaitingHarveyReview}",
                $"  IsCured={treatment.IsCured}",
                $"  IsCompleted={treatment.IsCompleted}",
                $"  ReadyForReviewDate={treatment.ReadyForReviewDate?.ToString() ?? "(null)"}",
                $"  hasBuffInGame={hasBuff}",
                $"  readyForReviewTopic={reviewTopic ?? "(n/a)"} active={reviewTopicActive}",
                $"  next step: {nextStep}",
            ]);
        }
    }
}
