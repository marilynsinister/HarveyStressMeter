using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Разделение legacy buffStressDarkness и новой системы уровней.</summary>
    public static class DarknessLegacyHelper
    {
        public static bool UsesLevelSystem(SaveData data, StateService stateService)
        {
            if (data.Darkness.FearLevel > 0)
                return true;

            if (data.Darkness.IsTherapyActive || data.Darkness.IsCured)
                return true;

            return stateService.HasBuffInGame(BuffIds.DarknessLevel1)
                   || stateService.HasBuffInGame(BuffIds.DarknessLevel2)
                   || stateService.HasBuffInGame(BuffIds.DarknessLevel3);
        }

        public static bool ShouldSkipLegacyDebuffSelector(SaveData data, StateService stateService, string buffId)
            => buffId == BuffIds.Darkness && UsesLevelSystem(data, stateService);

        /// <summary>Страх темноты по уровням ещё не в терапии и не излечен.</summary>
        public static bool NeedsHarveyDarknessTherapy(SaveData data, StateService stateService)
            => UsesLevelSystem(data, stateService)
               && !data.Darkness.IsTherapyActive
               && !data.Darkness.IsCured
               && data.Darkness.FearLevel > 0;

        public static bool IsDarknessLevelBuff(string? buffId) =>
            buffId is BuffIds.DarknessLevel1 or BuffIds.DarknessLevel2 or BuffIds.DarknessLevel3;

        /// <summary>Активный level-buff страха темноты (приоритет: 3 → 2 → 1).</summary>
        public static string? GetActiveLevelBuffId(StateService stateService)
        {
            if (stateService.HasBuffInGame(BuffIds.DarknessLevel3))
                return BuffIds.DarknessLevel3;

            if (stateService.HasBuffInGame(BuffIds.DarknessLevel2))
                return BuffIds.DarknessLevel2;

            if (stateService.HasBuffInGame(BuffIds.DarknessLevel1))
                return BuffIds.DarknessLevel1;

            return null;
        }

        /// <summary>Level-buff → канонический buffId для legacy StartTreatment.</summary>
        public static string ResolveTreatmentBuffId(string buffId) =>
            IsDarknessLevelBuff(buffId) ? BuffIds.Darkness : buffId;
    }

}
