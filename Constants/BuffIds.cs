namespace HarveyStressMeter.Constants
{
    /// <summary>
    /// ID баффов стресса и лечения
    /// </summary>
    public static class BuffIds
    {
        // Основные баффы стресса
        public const string Tired = "buffStressTired";
        public const string Lonely = "buffStressLonely";
        public const string Thunder = "buffStressThunder";
        public const string Darkness = "buffStressDarkness"; // Устаревший, теперь используются уровни
        public const string Hunger = "buffStressHunger";
        public const string Overwork = "buffStressOverwork";
        public const string NoSleep = "buffStressNoSleep";
        public const string TooCold = "buffStressTooCold";
        public const string Social = "buffStressSocial";
        public const string Criticism = "buffStressCriticism";
        public const string BadDream = "buffStressBadDream";
        public const string Panic = "buffStressPanic";
        public const string SleepDeprivation = "buffStressSleepDeprivation";
        public const string AnxietyWave = "buffStressAnxietyWave";
        public const string MentalFatigue = "buffStressMentalFatigue";
        public const string ShadowParanoia = "buffStressShadowParanoia";
        public const string FreezeResponse = "buffStressFreezeResponse";
        public const string Isolation = "buffStressIsolation";
        public const string Breakdown = "buffStressBreakdown";
        public const string Collapse = "buffStressCollapse";
        public const string Numbness = "buffStressNumbness";
        public const string Despair = "buffStressDespair";

        // Новые баффы темноты (система уровней)
        public const string DarknessLevel1 = "buffDarknessLevel1"; // Defense -1
        public const string DarknessLevel2 = "buffDarknessLevel2"; // Defense -2
        public const string DarknessLevel3 = "buffDarknessLevel3"; // Defense -3

        // Специальные баффы
        public const string Immunity = "buffStressImmunity";
        public const string CareAura = "HarveyStress.CareAura";
        /// <summary>Маркер programmatic trust-диалога (бафф не применяется).</summary>
        public const string TrustProgress = "HarveyStress.TrustProgress";
        public const string StressLoadTier = "buffStressLoadTier";

        // Баффы терапии темноты
        public const string DimLight = "buffDimLight"; // Приглушенный свет (Шаг 1)
        public const string HarveyLantern = "buffHarveyLantern"; // Фонарь от Харви: Defense +2 (Шаг 3)
        public const string DarknessOvercome = "buffDarknessOvercome"; // Перманентный: Defense +1 ночью

        // Баффы для квестов лечения
        public const string RestingAtHome = "buffRestingAtHome";
        public const string OverworkBreak = "buffOverworkBreak";
        public const string LightAndSafe = "buffLightAndSafe";
        public const string CalmingAtHospital = "buffCalmingAtHospitalWithHarvey";
    }
}

