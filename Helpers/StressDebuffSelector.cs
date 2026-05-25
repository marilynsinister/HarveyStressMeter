using System.Collections.Generic;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Единый приоритет и выбор одного untreated stress debuff за разговор с Харви.
    /// </summary>
    public static class StressDebuffSelector
    {
        /// <summary>Порядок приоритета untreated stress debuff'ов (высший — первый).</summary>
        public static readonly string[] PriorityOrder =
        {
            BuffIds.Social,
            BuffIds.Tired,
            BuffIds.Overwork,
            BuffIds.NoSleep,
            BuffIds.Lonely,
            BuffIds.Hunger,
            BuffIds.TooCold,
            BuffIds.Thunder,
        };

        public static readonly Dictionary<string, (string topic, int days)> BuffToStressTopic = new()
        {
            [BuffIds.Tired] = (TopicIds.StressTired, 2),
            [BuffIds.Lonely] = (TopicIds.StressLonely, 2),
            [BuffIds.Thunder] = (TopicIds.StressThunder, 1),
            [BuffIds.Hunger] = (TopicIds.StressHunger, 1),
            [BuffIds.Overwork] = (TopicIds.StressOverwork, 4),
            [BuffIds.NoSleep] = (TopicIds.StressNoSleep, 1),
            [BuffIds.TooCold] = (TopicIds.StressTooCold, 1),
            [BuffIds.Social] = (TopicIds.StressSocial, 1),
        };

        /// <summary>Дебафф активен, но лечение ещё не начато (consent/квест не выдан).</summary>
        public static bool IsUntreatedDebuff(StateService stateService, string buffId)
        {
            var treatment = stateService.GetActiveTreatment(buffId);

            if (treatment == null && stateService.HasBuffInGame(buffId))
                return true;

            if (treatment != null && !treatment.TreatmentStarted && !treatment.IsCured)
                return true;

            return false;
        }

        /// <summary>Все untreated debuff'ы в порядке приоритета.</summary>
        public static List<string> GetUntreatedDebuffs(StateService stateService)
        {
            var result = new List<string>();

            foreach (var buffId in PriorityOrder)
            {
                if (IsUntreatedDebuff(stateService, buffId))
                    result.Add(buffId);
            }

            return result;
        }

        /// <summary>Один debuff с наивысшим приоритетом или null.</summary>
        public static string? GetPrimaryUntreatedDebuff(StateService stateService)
        {
            foreach (var buffId in PriorityOrder)
            {
                if (IsUntreatedDebuff(stateService, buffId))
                    return buffId;
            }

            return null;
        }
    }
}
