namespace HarveyStressMeter.Constants
{
    /// <summary>
    /// «Границы»: что фермерша разрешает Харви. Когда доверие дорастает до нужного уровня,
    /// Харви сам спрашивает разрешения. Отказ — тоже ответ: он его уважает, и доверие от этого растёт.
    /// </summary>
    public static class HarveyCareConsents
    {
        /// <summary>Иногда заходить или спрашивать вечером, как она. Бонус к снижению стресса от лечения.</summary>
        public const string EveningChecks = "EveningChecks";

        /// <summary>Взять за руку во время флешбека. Бонус к стабилизации флешбека.</summary>
        public const string HoldHand = "HoldHand";

        /// <summary>Искать её, если она уйдёт в грозу. Без разрешения Харви в лес не придёт.</summary>
        public const string SearchInStorm = "SearchInStorm";

        /// <summary>Сказать «стоп» и увести домой, если она себя загоняет. Сильнее снижает набор стресса.</summary>
        public const string StrictCare = "StrictCare";

        /// <summary>
        /// Зеркальный вопрос в браке: Харви просит говорить ему, когда он перегибает с заботой.
        /// Тогда «строгий уход» — договор, а не право: штрафы доверия мягче, «стоп» работает чуть сильнее.
        /// </summary>
        public const string HarveyLimits = "HarveyLimits";

        /// <summary>Порядок, в котором Харви задаёт вопросы.</summary>
        public static readonly string[] All = { EveningChecks, HoldHand, SearchInStorm, StrictCare, HarveyLimits };

        /// <summary>Вопросы, которые Харви задаёт только мужем.</summary>
        public static bool RequiresMarriage(string consentId) => consentId == HarveyLimits;

        public static int RequiredTrustLevel(string consentId) => consentId switch
        {
            EveningChecks => HarveyCareTrustLevels.TrustedDoctor,
            HoldHand => HarveyCareTrustLevels.SafePerson,
            SearchInStorm => HarveyCareTrustLevels.SafePerson,
            StrictCare => HarveyCareTrustLevels.Anchor,
            HarveyLimits => HarveyCareTrustLevels.Anchor,
            _ => int.MaxValue,
        };
    }

    public enum HarveyConsentAnswer
    {
        /// <summary>Харви ещё не спрашивал — работает старое поведение (разрешено по уровню доверия).</summary>
        NotAsked = 0,
        Granted = 1,
        /// <summary>«Пока нет» — Харви спросит снова через ConsentReaskDays дней.</summary>
        NotYet = 2,
        /// <summary>«Нет, и не спрашивай» — больше не спросит.</summary>
        Never = 3,
    }
}
