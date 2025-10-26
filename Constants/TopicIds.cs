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

        // Топики перерывов (Overwork)
        public const string OverworkBreakActive = "topicOverworkBreakActive";
        public const string OverworkBreakInterrupted = "topicOverworkBreakInterrupted";

        // Топики начала лечения (устанавливаются при согласии на лечение в диалогах)
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
    }
}

