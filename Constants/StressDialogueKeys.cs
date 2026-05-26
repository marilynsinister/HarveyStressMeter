namespace HarveyStressMeter.Constants
{
    /// <summary>Ключи programmatic stress-диалогов (assets/stress_flow_dialogues.json).</summary>
    public static class StressDialogueKeys
    {
        public const string AmbientPrefix = "ambient_";

        public static string AmbientForCause(string causeId) => AmbientPrefix + causeId;

        public const string ReminderActiveTreatment1 = "reminder_active_treatment_1";
        public const string ReminderActiveTreatment2 = "reminder_active_treatment_2";
        public const string ReminderActiveTreatment3 = "reminder_active_treatment_3";

        public const string EpisodeStartSuffix = "_start";
        public const string EpisodeReviewSuffix = "_review";
        public const string WarTraumaSuffix = "_warTrauma";

        public static string EpisodeStart(string episodeId) => $"episode_{episodeId}_start";
        public static string EpisodeReview(string episodeId) => $"episode_{episodeId}_review";
        public static string EpisodeStartWarTrauma(string episodeId) => $"episode_{episodeId}_start_warTrauma";
        public static string EpisodeReviewWarTrauma(string episodeId) => $"episode_{episodeId}_review_warTrauma";

        // HarveyCareTrust — прогресс восстановления (не лечение, а навык)
        public const string TrustEarlyProfessional = "trust_early_professional";
        public const string TrustTrustedDoctor = "trust_trusted_doctor";
        public const string TrustSafePerson = "trust_safe_person";
        public const string TrustDatingGrounding = "trust_dating_grounding";
        public const string TrustMarriedAnchor = "trust_married_anchor";
        public const string TrustRecoveryRepeated = "trust_recovery_repeated";

        public static string TrustRescueForTier(string tier) => $"trust_rescue_{tier}";
    }
}
