using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.UI;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Services
{
    /// <summary>Тексты назначений для окна «План Харви» (не journal quest).</summary>
    internal static class HarveyPanelAssignmentFormatter
    {
        public sealed class Display
        {
            public static Display Empty { get; } = new();

            public bool HasAssignment { get; set; }
            public bool AwaitingHarveyReview { get; set; }
            public string StressTitle { get; set; } = "";
            public string ShortTitle { get; set; } = "";
            public string ObjectiveText { get; set; } = "";
            public string ProgressLine { get; set; } = "";
            public string AfterHint { get; set; } = "";
            public string StallHint { get; set; } = "";
        }

        public static Display Build(
            TreatmentEpisodeState? episode,
            TreatmentProgress? progress,
            int overworkBreaksToday)
        {
            if (episode == null || progress == null || !episode.IsActiveEpisode())
                return Display.Empty;

            if (!TreatmentEpisodeDefinitions.TryGet(episode.EpisodeId, out var definition))
                return Display.Empty;

            bool informal = HarveyPanelTexts.IsInformal;
            bool awaiting = episode.AwaitingHarveyReview;

            var display = episode.EpisodeId switch
            {
                StressEpisodes.AnxietySpike => BuildAnxiety(progress, awaiting, informal),
                StressEpisodes.PhysicalExhaustion => BuildPhysicalExhaustion(progress, episode, overworkBreaksToday, awaiting, informal),
                StressEpisodes.Burnout => BuildBurnout(progress, awaiting, informal),
                StressEpisodes.GotoroFlashback => BuildGotoro(progress, awaiting, informal),
                StressEpisodes.SocialShutdown => BuildSocialShutdown(awaiting, informal),
                _ => BuildGeneric(definition.DisplayName, progress, awaiting, informal),
            };

            display.HasAssignment = true;
            display.AwaitingHarveyReview = awaiting;
            display.ShortTitle = ShortenTitle(definition.DisplayName);
            return display;
        }

        private static string ShortenTitle(string displayName)
        {
            const string prefix = "Назначение Харви:";
            if (displayName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return displayName[prefix.Length..].Trim();

            return displayName.Trim();
        }

        private static Display BuildAnxiety(TreatmentProgress progress, bool awaiting, bool informal)
        {
            int current = System.Math.Min(progress.AnxietySafeSeconds, EpisodeQuestRules.AnxietySafeSecondsRequired);
            int required = EpisodeQuestRules.AnxietySafeSecondsRequired;
            bool complete = progress.AnxietySafeSeconds >= required;

            string progressLine = $"{HarveyPanelTexts.Stress.ProgressPrefix} {current}/{required} сек.";
            string description = informal
                ? "Харви просит переждать это в тихом месте. Дом, клиника, лес или спокойный угол подойдут."
                : "Харви просит переждать это в тихом месте. Дом, клиника, лес или спокойный угол подойдут.";

            if (awaiting || complete)
            {
                return new Display
                {
                    StressTitle = "Пик тревоги",
                    ObjectiveText = HarveyPanelTexts.Stress.AnxietyComplete,
                    ProgressLine = progressLine,
                    AfterHint = HarveyPanelTexts.TalkToHarvey(),
                };
            }

            string stallHint = current == 0
                ? (informal
                    ? HarveyPanelTexts.Stress.AnxietyStallHintInformal
                    : HarveyPanelTexts.Stress.AnxietyStallHint)
                : "";

            return new Display
            {
                StressTitle = "Пик тревоги",
                ObjectiveText = description,
                ProgressLine = progressLine,
                AfterHint = HarveyPanelTexts.AfterAssignmentTalk(),
                StallHint = stallHint,
            };
        }

        private static Display BuildPhysicalExhaustion(
            TreatmentProgress progress,
            TreatmentEpisodeState episode,
            int overworkBreaksToday,
            bool awaiting,
            bool informal)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HarveyPanelTexts.Tone(
                "Сделайте перерыв. Не «ещё один ряд грядок», а настоящий перерыв.",
                "Сделай перерыв. Не «ещё один ряд грядок», а настоящий."));

            sb.AppendLine();
            foreach (string causeId in episode.RelatedCauseIds)
            {
                bool done = progress.EpisodeCausesCompleted.Contains(causeId);
                sb.AppendLine(FormatPhysicalCauseLine(causeId, done, progress, overworkBreaksToday, informal));
            }

            if (awaiting)
            {
                sb.AppendLine();
                sb.AppendLine(HarveyPanelTexts.TalkToHarvey());
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine(HarveyPanelTexts.AfterAssignmentTalk());
            }

            string compact = EpisodeQuestProgressService.BuildCompactProgressLine(
                StressEpisodes.PhysicalExhaustion,
                progress,
                overworkBreaksToday,
                episode);

            return new Display
            {
                StressTitle = "Переутомление",
                ObjectiveText = sb.ToString().TrimEnd(),
                ProgressLine = string.IsNullOrWhiteSpace(compact)
                    ? ""
                    : $"{HarveyPanelTexts.Stress.ProgressPrefix} {compact}",
                AfterHint = awaiting ? HarveyPanelTexts.TalkToHarveySoon() : HarveyPanelTexts.AfterAssignmentTalk(),
            };
        }

        private static string FormatPhysicalCauseLine(
            string causeId,
            bool done,
            TreatmentProgress progress,
            int overworkBreaksToday,
            bool informal)
        {
            string mark = done ? "✓" : "○";
            return causeId switch
            {
                StressCauses.Hunger => $"{mark} {(informal ? "Съешь что-нибудь" : "Съешьте что-нибудь")}",
                StressCauses.TooCold => $"{mark} {(informal ? "Согрейся" : "Согрейтесь")} ({System.Math.Min(progress.WarmSeconds, EpisodeQuestRules.PhysicalWarmSecondsRequired)}/{EpisodeQuestRules.PhysicalWarmSecondsRequired} сек)",
                StressCauses.Tired => $"{mark} {(informal ? "Отдохни дома" : "Отдохните дома")} ({System.Math.Min(progress.TiredRestSeconds, EpisodeQuestRules.PhysicalTiredRestSecondsRequired)}/{EpisodeQuestRules.PhysicalTiredRestSecondsRequired} сек)",
                StressCauses.Overwork => $"{mark} {(informal ? "Сделай перерыв" : "Сделайте перерыв")} ({overworkBreaksToday}/{EpisodeQuestRules.PhysicalOverworkBreaksRequired})",
                StressCauses.NoSleep => $"{mark} {(informal ? "Лечь до 22:00" : "Лечь до 22:00")}",
                _ => $"{mark} {causeId}",
            };
        }

        private static Display BuildBurnout(TreatmentProgress progress, bool awaiting, bool informal)
        {
            string minesLine = progress.BurnoutAvoidedMinesToday
                ? (informal ? "✓ Сегодня без шахт" : "✓ Сегодня без шахт")
                : (informal
                    ? "○ Нужен день без шахт"
                    : "○ Нужен день без шахт");

            string body = $"""
                {HarveyPanelTexts.Tone("Сделайте перерыв. Не «ещё один ряд грядок», а настоящий перерыв.", "Сделай перерыв. Не «ещё один ряд грядок», а настоящий.")}

                {minesLine}
                ○ {HarveyPanelTexts.Tone("Лечь до 22:00", "Лечь до 22:00")}

                {HarveyPanelTexts.TalkToHarvey()}
                """;

            return new Display
            {
                StressTitle = "Переутомление",
                ObjectiveText = body.Trim(),
                ProgressLine = progress.BurnoutAvoidedMinesToday
                    ? $"{HarveyPanelTexts.Stress.ProgressPrefix} без шахт сегодня"
                    : $"{HarveyPanelTexts.Stress.ProgressPrefix} нужен день без шахт",
                AfterHint = awaiting ? HarveyPanelTexts.TalkToHarveySoon() : HarveyPanelTexts.AfterAssignmentTalk(),
            };
        }

        private static Display BuildGotoro(TreatmentProgress progress, bool awaiting, bool informal)
        {
            return new Display
            {
                StressTitle = HarveyPanelTexts.Tone("Вернитесь в настоящее", "Вернись в настоящее"),
                ObjectiveText = HarveyPanelTexts.Tone(
                    "Гроза пугает сильнее, чем кажется. Побудьте рядом с Харви или укройтесь в безопасном месте.",
                    "Гроза пугает сильнее, чем кажется. Побудь рядом с Харви или укройся в безопасном месте."),
                ProgressLine = $"{HarveyPanelTexts.Stress.ProgressPrefix} {progress.SecondsNearHarvey} сек укрытия",
                AfterHint = awaiting ? HarveyPanelTexts.TalkToHarveySoon() : HarveyPanelTexts.AfterAssignmentTalk(),
            };
        }

        private static Display BuildSocialShutdown(bool awaiting, bool informal)
        {
            return new Display
            {
                StressTitle = "Социальное напряжение",
                ObjectiveText = HarveyPanelTexts.Tone(
                    "Сегодня было слишком много людей. Харви просит снизить нагрузку и восстановиться.",
                    "Сегодня было слишком много людей. Харви просит снизить нагрузку и восстановиться."),
                AfterHint = awaiting ? HarveyPanelTexts.TalkToHarveySoon() : HarveyPanelTexts.AfterAssignmentTalk(),
            };
        }

        private static Display BuildGeneric(string displayName, TreatmentProgress progress, bool awaiting, bool informal)
        {
            return new Display
            {
                StressTitle = ShortenTitle(displayName),
                ObjectiveText = HarveyPanelTexts.Tone(
                    "Выполните назначение Харви и поговорите с ним.",
                    "Выполни назначение Харви и поговори с ним."),
                AfterHint = awaiting ? HarveyPanelTexts.TalkToHarveySoon() : HarveyPanelTexts.AfterAssignmentTalk(),
            };
        }
    }
}
