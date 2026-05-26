using System;
using System.Collections.Generic;
using System.Linq;
using HarveyStressMeter.Models;
using StardewValley;

namespace HarveyStressMeter.Constants
{
    public readonly struct EpisodeEvaluationContext
    {
        public int StressLoad { get; init; }
        public StressSeverity Severity { get; init; }
        public IReadOnlyCollection<string> ActiveCauseIds { get; init; }
        public bool GotoroFlashbackActive { get; init; }
        public bool WarTraumaFlag { get; init; }
        public bool IsLightning { get; init; }
        public bool HasActiveTreatment { get; init; }
        public bool AwaitingHarveyReview { get; init; }

        public bool HasCause(string causeId) =>
            ActiveCauseIds.Contains(causeId, StringComparer.Ordinal);

        public int CountRelatedCauses(IEnumerable<string> related) =>
            related.Count(HasCause);
    }

    /// <summary>Реестр TreatmentEpisode и логика триггеров.</summary>
    public static class TreatmentEpisodeDefinitions
    {
        public static readonly IReadOnlyList<TreatmentEpisodeDefinition> All = BuildAll();

        private static readonly Dictionary<string, TreatmentEpisodeDefinition> ById =
            All.ToDictionary(e => e.EpisodeId, StringComparer.Ordinal);

        public static bool TryGet(string episodeId, out TreatmentEpisodeDefinition definition)
            => ById.TryGetValue(episodeId, out definition!);

        public static TreatmentEpisodeDefinition? GetOrDefault(string episodeId) =>
            ById.GetValueOrDefault(episodeId);

        public static IReadOnlyList<TreatmentEpisodeDefinition> GetMatchingEpisodes(EpisodeEvaluationContext ctx)
        {
            return All.Where(def => MatchesTrigger(def, ctx)).ToList();
        }

        public static TreatmentEpisodeDefinition? SelectBestEpisode(EpisodeEvaluationContext ctx)
        {
            return GetMatchingEpisodes(ctx)
                .OrderByDescending(e => e.IsEmergency)
                .ThenByDescending(e => e.Priority)
                .FirstOrDefault();
        }

        public static bool MatchesTrigger(TreatmentEpisodeDefinition def, EpisodeEvaluationContext ctx)
        {
            if (ctx.StressLoad >= def.MinStressLoad)
                return HasAnyRelatedCause(def, ctx);

            return def.EpisodeId switch
            {
                StressEpisodes.PhysicalExhaustion =>
                    CountPhysicalCauses(ctx) >= 3,

                StressEpisodes.Burnout =>
                    ctx.HasCause(StressCauses.Overwork) && ctx.HasCause(StressCauses.NoSleep),

                StressEpisodes.AnxietySpike =>
                    ctx.HasCause(StressCauses.Thunder) && ctx.IsLightning,

                StressEpisodes.GotoroFlashback =>
                    ctx.GotoroFlashbackActive
                    || (ctx.HasCause(StressCauses.Thunder) && ctx.IsLightning && ctx.WarTraumaFlag),

                StressEpisodes.SocialShutdown =>
                    ctx.HasCause(StressCauses.Social) && ctx.HasCause(StressCauses.Lonely),

                _ => false,
            };
        }

        public static string? ResolvePrimaryCauseId(string episodeId, IEnumerable<string> activeCauseIds)
        {
            var buffId = ResolvePrimaryBuffId(episodeId, activeCauseIds);
            foreach (var (causeId, mappedBuffId) in StressCauses.CauseToBuff)
            {
                if (mappedBuffId == buffId)
                    return causeId;
            }

            return null;
        }

        public static string ResolvePrimaryBuffId(string episodeId, IEnumerable<string> activeCauseIds)
        {
            var causes = activeCauseIds.ToHashSet(StringComparer.Ordinal);
            if (!TryGet(episodeId, out var def))
                return BuffIds.Tired;

            return episodeId switch
            {
                StressEpisodes.PhysicalExhaustion => PickFirstCauseBuff(
                    causes,
                    StressCauses.NoSleep,
                    StressCauses.Overwork,
                    StressCauses.Tired,
                    StressCauses.Hunger,
                    StressCauses.TooCold) ?? def.DefaultPrimaryBuffId,

                StressEpisodes.Burnout => PickFirstCauseBuff(
                    causes,
                    StressCauses.Overwork,
                    StressCauses.NoSleep,
                    StressCauses.Tired) ?? def.DefaultPrimaryBuffId,

                StressEpisodes.AnxietySpike => PickFirstCauseBuff(
                    causes,
                    StressCauses.Thunder,
                    StressCauses.Darkness,
                    StressCauses.Social,
                    StressCauses.Lonely) ?? def.DefaultPrimaryBuffId,

                StressEpisodes.GotoroFlashback => BuffIds.Panic,

                StressEpisodes.SocialShutdown => PickFirstCauseBuff(
                    causes,
                    StressCauses.Social,
                    StressCauses.Lonely,
                    StressCauses.Tired) ?? def.DefaultPrimaryBuffId,

                _ => def.DefaultPrimaryBuffId,
            };
        }

        private static bool HasAnyRelatedCause(TreatmentEpisodeDefinition def, EpisodeEvaluationContext ctx) =>
            def.RelatedCauses.Any(ctx.HasCause);

        private static int CountPhysicalCauses(EpisodeEvaluationContext ctx) =>
            ctx.CountRelatedCauses(new[]
            {
                StressCauses.Hunger,
                StressCauses.NoSleep,
                StressCauses.TooCold,
                StressCauses.Tired,
                StressCauses.Overwork,
            });

        private static string? PickFirstCauseBuff(ISet<string> activeCauses, params string[] orderedCauses)
        {
            foreach (var cause in orderedCauses)
            {
                if (!activeCauses.Contains(cause))
                    continue;

                if (StressCauses.CauseToBuff.TryGetValue(cause, out var buffId))
                    return buffId;
            }

            return null;
        }

        private static List<TreatmentEpisodeDefinition> BuildAll() => new()
        {
            new TreatmentEpisodeDefinition
            {
                EpisodeId = StressEpisodes.PhysicalExhaustion,
                DisplayName = "Назначение Харви: восстановиться",
                RelatedCauses = new List<string>
                {
                    StressCauses.Hunger,
                    StressCauses.NoSleep,
                    StressCauses.TooCold,
                    StressCauses.Tired,
                    StressCauses.Overwork,
                },
                MinStressLoad = 50,
                MinSeverity = StressSeverity.High,
                RequiresHarveyTreatment = true,
                Priority = 70,
                QuestId = QuestIds.PhysicalExhaustion,
                DefaultPrimaryBuffId = BuffIds.NoSleep,
                StartDialogueKey = "episode_PhysicalExhaustion_start",
                ReminderDialogueKey = "episode_PhysicalExhaustion_reminder",
                ReadyForReviewDialogueKey = "episode_PhysicalExhaustion_review",
                CompletionDialogueKey = "episode_PhysicalExhaustion_complete",
                Objectives = new List<string>
                {
                    "Нормально поесть",
                    "Согреться, если активен TooCold",
                    "Лечь спать раньше, если активен NoSleep/Tired",
                    "Не перерабатывать сегодня, если активен Overwork",
                    "Поговорить с Харви",
                },
            },
            new TreatmentEpisodeDefinition
            {
                EpisodeId = StressEpisodes.Burnout,
                DisplayName = "Назначение Харви: остановиться",
                RelatedCauses = new List<string>
                {
                    StressCauses.Overwork,
                    StressCauses.NoSleep,
                    StressCauses.Tired,
                },
                MinStressLoad = 65,
                MinSeverity = StressSeverity.High,
                RequiresHarveyTreatment = true,
                Priority = 80,
                QuestId = QuestIds.Burnout,
                DefaultPrimaryBuffId = BuffIds.Overwork,
                StartDialogueKey = "episode_Burnout_start",
                ReminderDialogueKey = "episode_Burnout_reminder",
                ReadyForReviewDialogueKey = "episode_Burnout_review",
                CompletionDialogueKey = "episode_Burnout_complete",
                Objectives = new List<string>
                {
                    "Прекратить работу/шахты на сегодня",
                    "Лечь спать раньше",
                    "Поговорить с Харви",
                },
            },
            new TreatmentEpisodeDefinition
            {
                EpisodeId = StressEpisodes.AnxietySpike,
                DisplayName = "Назначение Харви: найти безопасное место",
                RelatedCauses = new List<string>
                {
                    StressCauses.Thunder,
                    StressCauses.Darkness,
                    StressCauses.Social,
                    StressCauses.Lonely,
                },
                MinStressLoad = 60,
                MinSeverity = StressSeverity.High,
                RequiresHarveyTreatment = true,
                Priority = 60,
                QuestId = QuestIds.AnxietySpike,
                DefaultPrimaryBuffId = BuffIds.Thunder,
                StartDialogueKey = "episode_AnxietySpike_start",
                ReminderDialogueKey = "episode_AnxietySpike_reminder",
                ReadyForReviewDialogueKey = "episode_AnxietySpike_review",
                CompletionDialogueKey = "episode_AnxietySpike_complete",
                Objectives = new List<string>
                {
                    "Уйти в безопасное/тихое место",
                    "Переждать пик тревоги",
                    "Поговорить с Харви",
                },
            },
            new TreatmentEpisodeDefinition
            {
                EpisodeId = StressEpisodes.GotoroFlashback,
                DisplayName = "Назначение Харви: вернуться в настоящее",
                RelatedCauses = new List<string>
                {
                    StressCauses.Thunder,
                    StressCauses.GotoroFlashback,
                },
                MinStressLoad = 75,
                MinSeverity = StressSeverity.Critical,
                RequiresHarveyTreatment = true,
                IsEmergency = true,
                Priority = 100,
                QuestId = QuestIds.GotoroFlashback,
                DefaultPrimaryBuffId = BuffIds.Panic,
                StartDialogueKey = "episode_GotoroFlashback_start",
                ReminderDialogueKey = "episode_GotoroFlashback_reminder",
                ReadyForReviewDialogueKey = "episode_GotoroFlashback_review",
                CompletionDialogueKey = "episode_GotoroFlashback_complete",
                Objectives = new List<string>
                {
                    "Уйти из города",
                    "Найти укрытие в лесу",
                    "Остаться там, пока дыхание не выровняется",
                    "Поговорить с Харви",
                },
            },
            new TreatmentEpisodeDefinition
            {
                EpisodeId = StressEpisodes.SocialShutdown,
                DisplayName = "Назначение Харви: не оставаться одной",
                RelatedCauses = new List<string>
                {
                    StressCauses.Social,
                    StressCauses.Lonely,
                    StressCauses.Tired,
                },
                MinStressLoad = 55,
                MinSeverity = StressSeverity.High,
                RequiresHarveyTreatment = true,
                Priority = 50,
                QuestId = QuestIds.SocialShutdown,
                DefaultPrimaryBuffId = BuffIds.Social,
                StartDialogueKey = "episode_SocialShutdown_start",
                ReminderDialogueKey = "episode_SocialShutdown_reminder",
                ReadyForReviewDialogueKey = "episode_SocialShutdown_review",
                CompletionDialogueKey = "episode_SocialShutdown_complete",
                Objectives = new List<string>
                {
                    "Побыть рядом с Харви или поговорить с одним доверенным человеком",
                    "Не перегружать себя разговорами",
                    "Поговорить с Харви",
                },
            },
        };
    }
}
