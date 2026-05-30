using System;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Снимает/ослабляет related causes после CompleteTreatmentEpisode.</summary>
    public static class EpisodeCauseResolver
    {
        public static int ApplyResolvedCauses(
            SaveData data,
            StressLoadService stressLoadService,
            BuffService buffService,
            StateService stateService,
            TreatmentEpisodeState episode)
        {
            var treatment = !string.IsNullOrEmpty(episode.PrimaryCauseId)
                && StressCauses.CauseToBuff.TryGetValue(episode.PrimaryCauseId, out var primaryBuff)
                ? stateService.GetActiveTreatment(primaryBuff)
                : null;

            var progress = treatment?.Progress;
            int totalReduction = 0;

            foreach (var causeId in episode.RelatedCauseIds)
            {
                if (!stressLoadService.GetActiveCauses().ContainsKey(causeId))
                    continue;

                var fullyResolve = episode.ObjectivesCompleted
                    || ShouldFullyResolveCause(data, buffService, causeId, progress);

                if (fullyResolve)
                {
                    totalReduction += StressCauses.GetBaseWeight(causeId);
                    stressLoadService.RemoveCause(causeId);

                    if (StressCauses.CauseToBuff.TryGetValue(causeId, out var buffId)
                        && buffService.HasBuff(buffId))
                    {
                        buffService.RemoveBuff(buffId);
                    }
                }
                else
                {
                    totalReduction += Math.Max(1, StressCauses.GetBaseWeight(causeId) / 2);
                }
            }

            if (totalReduction > 0)
                stressLoadService.DecayStress(totalReduction);

            return totalReduction;
        }

        private static bool ShouldFullyResolveCause(
            SaveData data,
            BuffService buffService,
            string causeId,
            TreatmentProgress? progress)
        {
            if (!StressCauses.CauseToBuff.TryGetValue(causeId, out var buffId))
                return true;

            if (!buffService.HasBuff(buffId))
                return true;

            return causeId switch
            {
                StressCauses.Hunger =>
                    data.DaysWithoutEating == 0
                    || progress?.AteAnyFood == true
                    || ConversationHelper.HasTopic(TopicIds.AteToday),

                StressCauses.NoSleep =>
                    progress?.EarlySleepStreak > 0,

                StressCauses.TooCold =>
                    (progress?.WarmSeconds ?? 0) >= 60
                    || GameStateHelper.IsInWarmZone(),

                StressCauses.Tired =>
                    (progress?.TiredRestSeconds ?? 0) >= 30,

                StressCauses.Overwork =>
                    data.OverworkBreaksToday >= 1,

                StressCauses.Social or StressCauses.Lonely => true,

                _ => false,
            };
        }
    }
}
