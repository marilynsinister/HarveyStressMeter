using System;
using System.Collections.Generic;
using System.Linq;
using HarveyOverhaul.Core.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.UI;
using StardewValley;

namespace HarveyStressMeter.Services;

/// <summary>Устарело: план рендерится в HarveyOverhaulCore через StressCareDirectiveProvider.</summary>
[Obsolete("Plan UI is built in HarveyOverhaulCore from IHarveyCareDirectiveProvider.")]
internal static class HarveyPanelPlanSectionsBuilder
{
    private const int PriorityPendingReview = 1;
    private const int PriorityActiveAssignment = 20;
    private const int PriorityLegacyTreatment = 50;
    private const int PriorityStressBuff = 70;

    public static List<HarveyPanelSectionDto> BuildAll(SaveData data, HandbookViewModel? handbook = null)
    {
        var sections = new List<HarveyPanelSectionDto>();
        var handledQuestIds = new HashSet<string>(StringComparer.Ordinal);
        var handledBuffIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var treatment in data.StressState.ActiveTreatments.Values
                     .Where(t => t.TreatmentStarted && !t.IsCompleted && !t.IsCured && t.AwaitingHarveyReview)
                     .OrderBy(t => ResolveTreatmentKey(t), StringComparer.Ordinal))
        {
            if (!handledQuestIds.Add(ResolveTreatmentKey(treatment)))
                continue;

            TrackHandledBuff(treatment, handledBuffIds);
            sections.Add(BuildPendingReviewSection(treatment));
        }

        var episode = data.ActiveTreatmentEpisode;
        if (episode != null
            && episode.IsActiveEpisode()
            && !episode.AwaitingHarveyReview
            && handledQuestIds.Add(episode.QuestId))
        {
            var treatment = data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            TrackHandledBuff(treatment, handledBuffIds);

            if (treatment?.Progress != null)
            {
                var assignment = HarveyPanelAssignmentFormatter.Build(
                    episode,
                    treatment.Progress,
                    data.OverworkBreaksToday);

                if (assignment.HasAssignment)
                    sections.Add(ToActiveAssignmentSection(episode.EpisodeId, assignment));
            }
        }

        foreach (var treatment in data.StressState.ActiveTreatments.Values
                     .Where(t => t.IsTreatmentActive() && !t.AwaitingHarveyReview)
                     .OrderBy(t => ResolveTreatmentKey(t), StringComparer.Ordinal))
        {
            if (!handledQuestIds.Add(ResolveTreatmentKey(treatment)))
                continue;

            TrackHandledBuff(treatment, handledBuffIds);

            if (episode != null
                && string.Equals(treatment.QuestId, episode.QuestId, StringComparison.Ordinal)
                && episode.IsActiveEpisode()
                && !episode.AwaitingHarveyReview)
            {
                continue;
            }

            sections.Add(BuildLegacySection(data, treatment));
        }

        foreach (var treatment in data.StressState.ActiveTreatments.Values
                     .Where(t => !t.IsCured && !t.IsCompleted && !t.TreatmentStarted && !t.AwaitingHarveyReview)
                     .OrderBy(t => ResolveTreatmentKey(t), StringComparer.Ordinal))
        {
            string key = ResolveTreatmentKey(treatment);
            if (string.IsNullOrWhiteSpace(key) || !handledQuestIds.Add(key))
                continue;

            TrackHandledBuff(treatment, handledBuffIds);
            sections.Add(BuildAwaitingStartSection(treatment));
        }

        AppendOpenJournalQuestSections(data, handledQuestIds, handledBuffIds, sections);
        AppendActiveStressBuffSections(handbook, handledBuffIds, sections);
        return sections;
    }

    private static string ResolveTreatmentKey(TreatmentState treatment)
        => !string.IsNullOrWhiteSpace(treatment.QuestId) ? treatment.QuestId : treatment.BuffId;

    /// <summary>Квесты в журнале игрока, не попавшие в ActiveTreatments-секции выше.</summary>
    private static void AppendOpenJournalQuestSections(
        SaveData data,
        HashSet<string> handledQuestIds,
        HashSet<string> handledBuffIds,
        List<HarveyPanelSectionDto> sections)
    {
        foreach (var quest in Game1.player.questLog)
        {
            if (quest == null || quest.completed.Value)
                continue;

            string questId = quest.id.Value;
            if (string.IsNullOrWhiteSpace(questId) || !handledQuestIds.Add(questId))
                continue;

            if (StressLegacyQuestMap.IsLegacyRecoveryQuest(questId))
            {
                string? buffId = StressLegacyQuestMap.TryGetBuffId(questId);
                var treatment = data.StressState.FindOpenTreatmentByQuest(questId)
                    ?? (buffId != null ? data.StressState.GetActiveTreatment(buffId) : null);

                if (treatment != null)
                {
                    TrackHandledBuff(treatment, handledBuffIds);
                    if (treatment.AwaitingHarveyReview)
                        sections.Add(BuildPendingReviewSection(treatment));
                    else if (treatment.IsTreatmentActive())
                        sections.Add(BuildLegacySection(data, treatment));
                    else
                        sections.Add(BuildAwaitingStartSection(treatment));
                }
                else
                {
                    sections.Add(BuildJournalQuestSection(questId, buffId, quest.questDescription));
                }

                continue;
            }

            if (!TreatmentEpisodeDefinitions.TryGet(
                    TreatmentEpisodeDefinitions.ResolveEpisodeIdForQuest(questId) ?? "",
                    out var episodeDef))
            {
                continue;
            }

            var episodeTreatment = data.StressState.FindOpenTreatmentByQuest(questId);
            if (episodeTreatment?.Progress == null)
            {
                sections.Add(new HarveyPanelSectionDto
                {
                    Title = ShortenEpisodeTitle(episodeDef.DisplayName),
                    Status = HarveyPanelTexts.Stress.StageInProgress,
                    Body = HarveyPanelTexts.Tone(
                        "Назначение из журнала активно. Подробности — клавиша H.",
                        "Назначение из журнала активно. Подробности — клавиша H."),
                    Priority = PriorityActiveAssignment,
                    Severity = HarveyPanelSeverity.Normal,
                });
                continue;
            }

            var pseudoEpisode = new TreatmentEpisodeState
            {
                EpisodeId = episodeDef.EpisodeId,
                QuestId = questId,
                TreatmentStarted = true,
            };
            var assignment = HarveyPanelAssignmentFormatter.Build(
                pseudoEpisode,
                episodeTreatment.Progress,
                data.OverworkBreaksToday);
            if (assignment.HasAssignment)
                sections.Add(ToActiveAssignmentSection(episodeDef.EpisodeId, assignment));
        }
    }

    private static HarveyPanelSectionDto BuildJournalQuestSection(
        string questId,
        string? buffId,
        string? journalDescription)
    {
        string title = StressLegacyQuestMap.GetDisplayName(buffId, questId);
        string body = HarveyPanelTexts.Tone(
            "Харви выдал назначение. Выполните задачу из журнала и вернитесь к нему.",
            "Харви выдал назначение. Выполни задачу из журнала и вернись к нему.");

        if (!string.IsNullOrWhiteSpace(journalDescription))
            body = $"{body}\n{journalDescription.Trim()}";

        return new HarveyPanelSectionDto
        {
            Title = title,
            Status = HarveyPanelTexts.Stress.StageInProgress,
            Body = body,
            Priority = PriorityLegacyTreatment,
            Severity = HarveyPanelSeverity.Normal,
        };
    }

    private static void TrackHandledBuff(TreatmentState? treatment, HashSet<string> handledBuffIds)
    {
        if (treatment == null || string.IsNullOrWhiteSpace(treatment.BuffId))
            return;

        handledBuffIds.Add(treatment.BuffId);
    }

    private static HarveyPanelSectionDto BuildAwaitingStartSection(TreatmentState treatment)
    {
        string contextTitle = ResolveAssignmentTitle(treatment.QuestId, treatment.BuffId);

        return new HarveyPanelSectionDto
        {
            Title = contextTitle,
            Status = "Ожидает назначения",
            Body = HarveyPanelTexts.Tone(
                "Харви уже видит признаки перегруза. Поговорите с ним, чтобы начать лечение.",
                "Харви уже видит признаки перегруза. Поговори с ним, чтобы начать лечение."),
            Priority = PriorityLegacyTreatment + 5,
            Severity = HarveyPanelSeverity.Warning,
        };
    }

    private static void AppendActiveStressBuffSections(
        HandbookViewModel? handbook,
        HashSet<string> handledBuffIds,
        List<HarveyPanelSectionDto> sections)
    {
        if (handbook == null || handbook.ActiveStates.Count == 0)
            return;

        foreach (var row in handbook.ActiveStates)
        {
            if (string.IsNullOrWhiteSpace(row.BuffId) || !handledBuffIds.Add(row.BuffId))
                continue;

            sections.Add(new HarveyPanelSectionDto
            {
                Title = row.Title,
                Status = "Стресс",
                Body = string.IsNullOrWhiteSpace(row.CureSummary)
                    ? row.Effects
                    : row.CureSummary,
                Priority = PriorityStressBuff,
                Severity = HarveyPanelSeverity.Info,
            });
        }
    }

    private static HarveyPanelSectionDto BuildPendingReviewSection(TreatmentState treatment)
    {
        string contextTitle = ResolveAssignmentTitle(treatment.QuestId, treatment.BuffId);

        return new HarveyPanelSectionDto
        {
            Title = "Поговорите с Харви",
            Status = "Назначение выполнено",
            Body = string.IsNullOrWhiteSpace(contextTitle)
                ? HarveyPanelTexts.TalkToHarvey()
                : $"{contextTitle}. {HarveyPanelTexts.TalkToHarvey()}",
            Priority = PriorityPendingReview,
            Severity = HarveyPanelSeverity.Urgent,
        };
    }

    private static HarveyPanelSectionDto ToActiveAssignmentSection(
        string episodeId,
        HarveyPanelAssignmentFormatter.Display assignment)
    {
        string body = assignment.ObjectiveText;
        if (!string.IsNullOrWhiteSpace(assignment.AfterHint))
            body = JoinLines(body, assignment.AfterHint);

        return new HarveyPanelSectionDto
        {
            Title = ResolvePlanTitle(episodeId, assignment),
            Status = assignment.ProgressLine,
            Body = body,
            Priority = PriorityActiveAssignment,
            Severity = HarveyPanelSeverity.Normal,
        };
    }

    private static HarveyPanelSectionDto BuildLegacySection(SaveData data, TreatmentState treatment)
    {
        var progress = treatment.Progress;
        string? episodeId = TreatmentEpisodeDefinitions.ResolveEpisodeIdForQuest(treatment.QuestId);

        if (string.Equals(treatment.QuestId, QuestIds.SocialShutdown, StringComparison.Ordinal)
            || string.Equals(episodeId, StressEpisodes.SocialShutdown, StringComparison.Ordinal))
        {
            return new HarveyPanelSectionDto
            {
                Title = "Не оставаться одной",
                Status = CompactProgress(progress.GetSocialShutdownProgressText()),
                Body = HarveyPanelTexts.Tone(
                    "Мягкий контакт, без толпы. Поговорите с теми, с кем безопасно.",
                    "Мягкий контакт, без толпы. Поговори с теми, с кем безопасно."),
                Priority = PriorityActiveAssignment,
                Severity = HarveyPanelSeverity.Normal,
            };
        }

        if (string.Equals(treatment.QuestId, QuestIds.AnxietySpike, StringComparison.Ordinal)
            || string.Equals(episodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal))
        {
            var pseudoEpisode = new TreatmentEpisodeState
            {
                EpisodeId = StressEpisodes.AnxietySpike,
                QuestId = QuestIds.AnxietySpike,
                TreatmentStarted = true,
                AwaitingHarveyReview = treatment.AwaitingHarveyReview,
            };

            var assignment = HarveyPanelAssignmentFormatter.Build(
                pseudoEpisode,
                progress,
                data.OverworkBreaksToday);

            if (assignment.HasAssignment)
                return ToActiveAssignmentSection(StressEpisodes.AnxietySpike, assignment);
        }

        if (string.Equals(treatment.QuestId, QuestIds.Social, StringComparison.Ordinal)
            || string.Equals(treatment.BuffId, BuffIds.Social, StringComparison.OrdinalIgnoreCase))
        {
            return new HarveyPanelSectionDto
            {
                Title = "Социальная тревожность",
                Status = CompactProgress(progress.GetSocialProgressText()),
                Body = HarveyPanelTexts.Tone(
                    "Выполните назначение из журнала и вернитесь к Харви.",
                    "Выполни назначение из журнала и вернись к Харви."),
                Priority = PriorityLegacyTreatment,
                Severity = HarveyPanelSeverity.Normal,
            };
        }

        if (string.Equals(treatment.BuffId, BuffIds.Hunger, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment.QuestId, QuestIds.Hunger, StringComparison.Ordinal))
        {
            return BuildSimpleLegacySection(
                "Слабость от голода",
                progress.AteAnyFood ? "Еда принята" : "Нужно поесть",
                progress.AteAnyFood
                    ? LegacyTreatmentObjectives.HungerDone
                    : "Съешьте любую еду и вернитесь к Харви.");
        }

        if (string.Equals(treatment.BuffId, BuffIds.NoSleep, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment.QuestId, QuestIds.NoSleep, StringComparison.Ordinal))
        {
            return BuildSimpleLegacySection(
                "Недосып",
                progress.EarlySleepStreak > 0 ? "Ранний отбой засчитан" : "Нужен ранний сон",
                progress.EarlySleepStreak > 0
                    ? LegacyTreatmentObjectives.NoSleepDone
                    : LegacyTreatmentObjectives.NoSleepDefault);
        }

        if (string.Equals(treatment.BuffId, BuffIds.Tired, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment.QuestId, QuestIds.Tired, StringComparison.Ordinal))
        {
            return BuildSimpleLegacySection(
                "Усталость",
                $"Отдых дома: {progress.TiredRestSeconds} сек.",
                HarveyPanelTexts.Tone(
                    "Отдохните дома без тяжёлой работы.",
                    "Отдохни дома без тяжёлой работы."));
        }

        if (string.Equals(treatment.BuffId, BuffIds.Lonely, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment.QuestId, QuestIds.Lonely, StringComparison.Ordinal))
        {
            return BuildSimpleLegacySection(
                "Одиночество",
                $"Разговоров: {progress.TalkedUniqueToday}",
                LegacyTreatmentObjectives.Lonely(progress.TalkedUniqueToday));
        }

        if (string.Equals(treatment.BuffId, BuffIds.Overwork, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment.QuestId, QuestIds.Overwork, StringComparison.Ordinal))
        {
            return BuildSimpleLegacySection(
                "Переработка",
                HarveyPanelTexts.Stress.StageInProgress,
                LegacyTreatmentObjectives.OverworkDailyStart);
        }

        if (string.Equals(treatment.BuffId, BuffIds.TooCold, StringComparison.OrdinalIgnoreCase)
            || string.Equals(treatment.QuestId, QuestIds.TooCold, StringComparison.Ordinal))
        {
            return BuildSimpleLegacySection(
                "Переохлаждение",
                $"Согревание: {progress.WarmSeconds} сек.",
                LegacyTreatmentObjectives.TooColdWarm(progress.WarmSeconds));
        }

        if (!string.IsNullOrWhiteSpace(episodeId)
            && TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
        {
            var pseudoEpisode = new TreatmentEpisodeState
            {
                EpisodeId = episodeId,
                QuestId = treatment.QuestId,
                TreatmentStarted = true,
                RelatedCauseIds = definition.RelatedCauses.ToList(),
            };

            var assignment = HarveyPanelAssignmentFormatter.Build(
                pseudoEpisode,
                progress,
                data.OverworkBreaksToday);

            if (assignment.HasAssignment)
                return ToActiveAssignmentSection(episodeId, assignment);
        }

        return new HarveyPanelSectionDto
        {
            Title = ResolveAssignmentTitle(treatment.QuestId, treatment.BuffId),
            Status = HarveyPanelTexts.Stress.StageInProgress,
            Body = HarveyPanelTexts.Tone(
                "Выполните назначение из журнала и вернитесь к Харви.",
                "Выполни назначение из журнала и вернись к Харви."),
            Priority = PriorityLegacyTreatment,
            Severity = HarveyPanelSeverity.Normal,
        };
    }

    private static string ResolvePlanTitle(
        string episodeId,
        HarveyPanelAssignmentFormatter.Display assignment)
    {
        if (string.Equals(episodeId, StressEpisodes.SocialShutdown, StringComparison.Ordinal))
            return "Не оставаться одной";

        if (string.Equals(episodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal))
            return HarveyPanelTexts.Plan.StressAssignmentTitle;

        if (!string.IsNullOrWhiteSpace(assignment.ShortTitle))
            return assignment.ShortTitle;

        if (!string.IsNullOrWhiteSpace(assignment.StressTitle))
            return assignment.StressTitle;

        return HarveyPanelTexts.Plan.StressAssignmentTitle;
    }

    private static string ResolveAssignmentTitle(string questId, string buffId)
    {
        string? episodeId = TreatmentEpisodeDefinitions.ResolveEpisodeIdForQuest(questId);
        if (!string.IsNullOrWhiteSpace(episodeId)
            && TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
        {
            return ShortenEpisodeTitle(definition.DisplayName);
        }

        return questId switch
        {
            _ when string.Equals(questId, QuestIds.SocialShutdown, StringComparison.Ordinal) => "Не оставаться одной",
            _ when string.Equals(questId, QuestIds.Social, StringComparison.Ordinal) => "Социальная тревожность",
            _ when string.Equals(questId, QuestIds.AnxietySpike, StringComparison.Ordinal) => "Пик тревоги",
            _ when string.Equals(questId, QuestIds.GotoroFlashback, StringComparison.Ordinal) => "Укрытие при flashback",
            _ => buffId,
        };
    }

    private static string ShortenEpisodeTitle(string displayName)
    {
        const string prefix = "Назначение Харви:";
        if (displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return displayName[prefix.Length..].Trim();

        return displayName.Trim();
    }

    private static string CompactProgress(string progress)
        => progress.Replace("\n", " | ", StringComparison.Ordinal);

    private static HarveyPanelSectionDto BuildSimpleLegacySection(
        string title,
        string status,
        string body)
        => new()
        {
            Title = title,
            Status = status,
            Body = body,
            Priority = PriorityLegacyTreatment,
            Severity = HarveyPanelSeverity.Normal,
        };

    private static string JoinLines(params string?[] lines)
        => string.Join("\n", lines.Where(line => !string.IsNullOrWhiteSpace(line)));
}
