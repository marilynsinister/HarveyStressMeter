using System.Collections.Generic;

namespace HarveyStressMeter.Constants
{
    /// <summary>Идентификаторы причин стресса (StressCause).</summary>
    public static class StressCauses
    {
        public const string Hunger = "Hunger";
        public const string NoSleep = "NoSleep";
        public const string TooCold = "TooCold";
        public const string Thunder = "Thunder";
        public const string ThunderRelapse = "ThunderRelapse";
        public const string ThunderSensitivity = "ThunderSensitivity";
        public const string Overwork = "Overwork";
        public const string Tired = "Tired";
        public const string Darkness = "Darkness";
        public const string Lonely = "Lonely";
        public const string Social = "Social";
        public const string GotoroFlashback = "GotoroFlashback";

        /// <summary>Базовые веса причин в StressLoad.</summary>
        public static readonly Dictionary<string, int> BaseWeights = new(StringComparer.Ordinal)
        {
            [Hunger] = 10,
            [Tired] = 10,
            [TooCold] = 15,
            [Darkness] = 10,
            [Social] = 10,
            [Lonely] = 15,
            [Overwork] = 20,
            [NoSleep] = 25,
            [Thunder] = 30,
            [ThunderRelapse] = 15,
            [ThunderSensitivity] = 10,
            [GotoroFlashback] = 50,
        };

        /// <summary>Маппинг buffId → StressCause.</summary>
        public static readonly Dictionary<string, string> BuffToCause = new(StringComparer.Ordinal)
        {
            [BuffIds.Hunger] = Hunger,
            [BuffIds.Tired] = Tired,
            [BuffIds.TooCold] = TooCold,
            [BuffIds.Darkness] = Darkness,
            [BuffIds.Social] = Social,
            [BuffIds.Lonely] = Lonely,
            [BuffIds.Overwork] = Overwork,
            [BuffIds.NoSleep] = NoSleep,
            [BuffIds.Thunder] = Thunder,
            [BuffIds.DarknessLevel1] = Darkness,
            [BuffIds.DarknessLevel2] = Darkness,
            [BuffIds.DarknessLevel3] = Darkness,
        };

        /// <summary>Обратный маппинг StressCause → канонический buffId.</summary>
        public static readonly Dictionary<string, string> CauseToBuff = new(StringComparer.Ordinal)
        {
            [Hunger] = BuffIds.Hunger,
            [Tired] = BuffIds.Tired,
            [TooCold] = BuffIds.TooCold,
            [Darkness] = BuffIds.Darkness,
            [Social] = BuffIds.Social,
            [Lonely] = BuffIds.Lonely,
            [Overwork] = BuffIds.Overwork,
            [NoSleep] = BuffIds.NoSleep,
            [Thunder] = BuffIds.Thunder,
            [GotoroFlashback] = BuffIds.Panic,
        };

        public static bool TryGetCauseForBuff(string buffId, out string causeId)
            => BuffToCause.TryGetValue(buffId, out causeId!);

        public static int GetBaseWeight(string causeId)
            => BaseWeights.GetValueOrDefault(causeId, 10);

        public static bool CanSelfResolve(string causeId) =>
            causeId is Hunger or Tired or TooCold or Darkness or Social or Lonely
                or ThunderRelapse or ThunderSensitivity;

        public static bool RequiresHarveyIfSevere(string causeId) =>
            causeId is NoSleep or Overwork or Thunder or GotoroFlashback;

        public static bool IsSevereCause(string causeId) =>
            RequiresHarveyIfSevere(causeId) || GetBaseWeight(causeId) >= 20;
    }
}
