using System.Collections.Generic;
using System.Linq;

namespace HarveyStressMeter.Constants
{
    /// <summary>
    /// Реализованные в C# gameplay stress buff'ы и связанные CP topic ID.
    /// Treatment start is controlled only by C# consent flow. CP topics must not start treatment.
    /// </summary>
    public static class TreatmentTopics
    {
        /// <summary>Buff'ы с полным C# pipeline (триггер → consent → квест → cured).</summary>
        public static readonly string[] ImplementedBuffIds =
        {
            BuffIds.Social,
            BuffIds.Tired,
            BuffIds.Overwork,
            BuffIds.NoSleep,
            BuffIds.Lonely,
            BuffIds.Hunger,
            BuffIds.TooCold,
            BuffIds.Thunder,
            BuffIds.Darkness,
        };

        /// <summary>Legacy CP triggers (topicStressTreatmentXXXStarted) — cleanup only.</summary>
        public static readonly IReadOnlyDictionary<string, string> LegacyStartByBuff = new Dictionary<string, string>
        {
            [BuffIds.Tired] = TopicIds.TreatmentStartTired,
            [BuffIds.Lonely] = TopicIds.TreatmentStartLonely,
            [BuffIds.Thunder] = TopicIds.TreatmentStartThunder,
            [BuffIds.Hunger] = TopicIds.TreatmentStartHunger,
            [BuffIds.Overwork] = TopicIds.TreatmentStartOverwork,
            [BuffIds.NoSleep] = TopicIds.TreatmentStartNoSleep,
            [BuffIds.TooCold] = TopicIds.TreatmentStartTooCold,
            [BuffIds.Social] = TopicIds.TreatmentStartSocial,
            [BuffIds.Darkness] = TopicIds.TreatmentStartDarkness,
            [BuffIds.Criticism] = TopicIds.TreatmentStartCriticism,
            [BuffIds.BadDream] = TopicIds.TreatmentStartBadDream,
            [BuffIds.Panic] = TopicIds.TreatmentStartPanic,
            [BuffIds.SleepDeprivation] = TopicIds.TreatmentStartSleepDeprivation,
            [BuffIds.AnxietyWave] = TopicIds.TreatmentStartAnxietyWave,
            [BuffIds.MentalFatigue] = TopicIds.TreatmentStartMentalFatigue,
            [BuffIds.ShadowParanoia] = TopicIds.TreatmentStartShadowParanoia,
            [BuffIds.FreezeResponse] = TopicIds.TreatmentStartFreezeResponse,
            [BuffIds.Isolation] = TopicIds.TreatmentStartIsolation,
            [BuffIds.Breakdown] = TopicIds.TreatmentStartBreakdown,
            [BuffIds.Collapse] = TopicIds.TreatmentStartCollapse,
            [BuffIds.Numbness] = TopicIds.TreatmentStartNumbness,
            [BuffIds.Despair] = TopicIds.TreatmentStartDespair,
        };

        /// <summary>Aftercare dialogue topics — set by C# StartTreatment after consent (implemented buffs only).</summary>
        public static readonly IReadOnlyDictionary<string, string> FollowupByBuff = new Dictionary<string, string>
        {
            [BuffIds.Tired] = TopicIds.TreatmentFollowupTired,
            [BuffIds.Lonely] = TopicIds.TreatmentFollowupLonely,
            [BuffIds.Thunder] = TopicIds.TreatmentFollowupThunder,
            [BuffIds.Hunger] = TopicIds.TreatmentFollowupHunger,
            [BuffIds.Overwork] = TopicIds.TreatmentFollowupOverwork,
            [BuffIds.NoSleep] = TopicIds.TreatmentFollowupNoSleep,
            [BuffIds.TooCold] = TopicIds.TreatmentFollowupTooCold,
            [BuffIds.Social] = TopicIds.TreatmentFollowupSocial,
            [BuffIds.Darkness] = TopicIds.TreatmentFollowupDarkness,
        };

        /// <summary>
        /// CP topic ID для нереализованных stress-механик — удаляются из save при cleanup.
        /// Тексты сохранены в dialoguesHarveyCureStress.json, но отключены CP-патчем.
        /// </summary>
        public static readonly string[] UnimplementedDisabledTopicIds =
        {
            "topicStressCriticism",
            "topicStressBadDream",
            "topicStressPanic",
            "topicStressSleepDeprivation",
            "topicStressAnxietyWave",
            "topicStressMentalFatigue",
            "topicStressShadowParanoia",
            "topicStressFreezeResponse",
            "topicStressIsolation",
            "topicStressBreakdown",
            "topicStressCollapse",
            "topicStressNumbness",
            "topicStressDespair",
            "topicStressCritical",
            TopicIds.TreatmentFollowupCriticism,
            TopicIds.TreatmentFollowupBadDream,
            TopicIds.TreatmentFollowupPanic,
            TopicIds.TreatmentFollowupSleepDeprivation,
            TopicIds.TreatmentFollowupAnxietyWave,
            TopicIds.TreatmentFollowupMentalFatigue,
            TopicIds.TreatmentFollowupShadowParanoia,
            TopicIds.TreatmentFollowupFreezeResponse,
            TopicIds.TreatmentFollowupIsolation,
            TopicIds.TreatmentFollowupBreakdown,
            TopicIds.TreatmentFollowupCollapse,
            TopicIds.TreatmentFollowupNumbness,
            TopicIds.TreatmentFollowupDespair,
            "topicStressTreatmentCriticismCured",
            "topicStressTreatmentBadDreamCured",
            "topicStressTreatmentPanicCured",
            "topicStressTreatmentSleepDeprivationCured",
            "topicStressTreatmentAnxietyWaveCured",
            "topicStressTreatmentMentalFatigueCured",
            "topicStressTreatmentShadowParanoiaCured",
            "topicStressTreatmentFreezeResponseCured",
            "topicStressTreatmentIsolationCured",
            "topicStressTreatmentBreakdownCured",
            "topicStressTreatmentCollapseCured",
            "topicStressTreatmentNumbnessCured",
            "topicStressTreatmentDespairCured",
            TopicIds.TreatmentStartCriticism,
            TopicIds.TreatmentStartBadDream,
            TopicIds.TreatmentStartPanic,
            TopicIds.TreatmentStartSleepDeprivation,
            TopicIds.TreatmentStartAnxietyWave,
            TopicIds.TreatmentStartMentalFatigue,
            TopicIds.TreatmentStartShadowParanoia,
            TopicIds.TreatmentStartFreezeResponse,
            TopicIds.TreatmentStartIsolation,
            TopicIds.TreatmentStartBreakdown,
            TopicIds.TreatmentStartCollapse,
            TopicIds.TreatmentStartNumbness,
            TopicIds.TreatmentStartDespair,
        };

        /// <summary>Топик финального разговора после выполнения условий лечения (CP + C# review).</summary>
        public static readonly IReadOnlyDictionary<string, string> ReadyForReviewByBuff = new Dictionary<string, string>
        {
            [BuffIds.Tired] = "topicStressTreatmentTiredReadyForReview",
            [BuffIds.Lonely] = "topicStressTreatmentLonelyReadyForReview",
            [BuffIds.Thunder] = "topicStressTreatmentThunderReadyForReview",
            [BuffIds.Hunger] = "topicStressTreatmentHungerReadyForReview",
            [BuffIds.Overwork] = "topicStressTreatmentOverworkReadyForReview",
            [BuffIds.NoSleep] = "topicStressTreatmentNoSleepReadyForReview",
            [BuffIds.TooCold] = "topicStressTreatmentTooColdReadyForReview",
            [BuffIds.Social] = "topicStressTreatmentSocialReadyForReview",
            [BuffIds.Darkness] = "topicStressTreatmentDarknessReadyForReview",
        };

        public static string? GetReadyForReviewTopic(string buffId)
            => ReadyForReviewByBuff.TryGetValue(buffId, out var topic) ? topic : null;

        public static bool IsImplementedBuff(string buffId)
            => ImplementedBuffIds.Contains(buffId);
    }
}
