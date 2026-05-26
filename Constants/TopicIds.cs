namespace HarveyStressMeter.Constants
{
    /// <summary>
    /// ID топиков разговоров
    /// </summary>
    public static class TopicIds
    {
        // Служебные топики
        public const string SpokeToday = "topicSpeakToSomebody";
        public const string AteToday = "topicEatSomething";
        public const string LonelyPending = "topicStressLonely_Pending";
        public const string TreatmentStarted = "topicStressTreatmentStarted";

        // Топики стресса
        public const string StressTired = "topicStressTired";
        public const string StressLonely = "topicStressLonely";
        public const string StressThunder = "topicStressThunder";
        public const string StressHunger = "topicStressHunger";
        public const string StressOverwork = "topicStressOverwork";
        public const string StressNoSleep = "topicStressNoSleep";
        public const string StressTooCold = "topicStressTooCold";
        public const string StressDarkness = "topicStressDarkness";
        public const string StressSocial = "topicStressSocial";

        /// <summary>Gotoro thunder flashback активен — CP gate для forest rescue.</summary>
        public const string GotoroFlashbackActive = "topicStressGotoroFlashbackActive";

        /// <summary>C# выставляет перед запуском rescue-сцены (предотвращает дубль location trigger).</summary>
        public const string GotoroForestRescuePending = "topicStressGotoroForestRescuePending";

        /// <summary>После forest rescue — programmatic follow-up при разговоре с Харви.</summary>
        public const string TrustRescueMidTrust = "topicStressTrust_RescueMidTrust";
        public const string TrustRescueHighTrust = "topicStressTrust_RescueHighTrust";
        public const string TrustRescueDating = "topicStressTrust_RescueDating";
        public const string TrustRescueMarried = "topicStressTrust_RescueMarried";

        // Топики перерывов (Overwork)
        public const string OverworkBreakActive = "topicOverworkBreakActive";
        public const string OverworkBreakInterrupted = "topicOverworkBreakInterrupted";

        // Топики начала лечения (legacy CP — НЕ gameplay-триггеры; только cleanup)
        public const string TreatmentStartTired = "topicStressTreatmentTiredStarted";
        public const string TreatmentStartLonely = "topicStressTreatmentLonelyStarted";
        public const string TreatmentStartThunder = "topicStressTreatmentThunderStarted";
        public const string TreatmentStartHunger = "topicStressTreatmentHungerStarted";
        public const string TreatmentStartOverwork = "topicStressTreatmentOverworkStarted";
        public const string TreatmentStartNoSleep = "topicStressTreatmentNoSleepStarted";
        public const string TreatmentStartTooCold = "topicStressTreatmentTooColdStarted";
        public const string TreatmentStartSocial = "topicStressTreatmentSocialStarted";
        public const string TreatmentStartDarkness = "topicStressTreatmentDarknessStarted";
        public const string TreatmentStartCriticism = "topicStressTreatmentCriticismStarted";
        public const string TreatmentStartBadDream = "topicStressTreatmentBadDreamStarted";
        public const string TreatmentStartPanic = "topicStressTreatmentPanicStarted";
        public const string TreatmentStartSleepDeprivation = "topicStressTreatmentSleepDeprivationStarted";
        public const string TreatmentStartAnxietyWave = "topicStressTreatmentAnxietyWaveStarted";
        public const string TreatmentStartMentalFatigue = "topicStressTreatmentMentalFatigueStarted";
        public const string TreatmentStartShadowParanoia = "topicStressTreatmentShadowParanoiaStarted";
        public const string TreatmentStartFreezeResponse = "topicStressTreatmentFreezeResponseStarted";
        public const string TreatmentStartIsolation = "topicStressTreatmentIsolationStarted";
        public const string TreatmentStartBreakdown = "topicStressTreatmentBreakdownStarted";
        public const string TreatmentStartCollapse = "topicStressTreatmentCollapseStarted";
        public const string TreatmentStartNumbness = "topicStressTreatmentNumbnessStarted";
        public const string TreatmentStartDespair = "topicStressTreatmentDespairStarted";

        // Followup-топики после C# consent (CP aftercare-реплики, не старт лечения)
        public const string TreatmentFollowupTired = "topicStressTreatmentTiredFollowup";
        public const string TreatmentFollowupLonely = "topicStressTreatmentLonelyFollowup";
        public const string TreatmentFollowupThunder = "topicStressTreatmentThunderFollowup";
        public const string TreatmentFollowupHunger = "topicStressTreatmentHungerFollowup";
        public const string TreatmentFollowupOverwork = "topicStressTreatmentOverworkFollowup";
        public const string TreatmentFollowupNoSleep = "topicStressTreatmentNoSleepFollowup";
        public const string TreatmentFollowupTooCold = "topicStressTreatmentTooColdFollowup";
        public const string TreatmentFollowupSocial = "topicStressTreatmentSocialFollowup";
        public const string TreatmentFollowupDarkness = "topicStressTreatmentDarknessFollowup";
        public const string TreatmentFollowupCriticism = "topicStressTreatmentCriticismFollowup";
        public const string TreatmentFollowupBadDream = "topicStressTreatmentBadDreamFollowup";
        public const string TreatmentFollowupPanic = "topicStressTreatmentPanicFollowup";
        public const string TreatmentFollowupSleepDeprivation = "topicStressTreatmentSleepDeprivationFollowup";
        public const string TreatmentFollowupAnxietyWave = "topicStressTreatmentAnxietyWaveFollowup";
        public const string TreatmentFollowupMentalFatigue = "topicStressTreatmentMentalFatigueFollowup";
        public const string TreatmentFollowupShadowParanoia = "topicStressTreatmentShadowParanoiaFollowup";
        public const string TreatmentFollowupFreezeResponse = "topicStressTreatmentFreezeResponseFollowup";
        public const string TreatmentFollowupIsolation = "topicStressTreatmentIsolationFollowup";
        public const string TreatmentFollowupBreakdown = "topicStressTreatmentBreakdownFollowup";
        public const string TreatmentFollowupCollapse = "topicStressTreatmentCollapseFollowup";
        public const string TreatmentFollowupNumbness = "topicStressTreatmentNumbnessFollowup";
        public const string TreatmentFollowupDespair = "topicStressTreatmentDespairFollowup";
    }
}

