using System.Collections.Generic;
using HarveyStressMeter.Constants;

namespace HarveyStressMeter.Models
{
    /// <summary>Доверие к Харви как «safe person» — отдельно от friendship hearts.</summary>
    public sealed class HarveyCareTrustState
    {
        public int TrustPoints { get; set; }

        public int TrustLevel { get; set; }

        public int SuccessfulAssignments { get; set; }

        public int IgnoredAssignments { get; set; }

        public int FlashbacksStabilizedWithHarvey { get; set; }

        public int DaysSinceLastSuccessfulAssignment { get; set; }

        public int LastTrustGainDay { get; set; }

        public int LastTrustPenaltyDay { get; set; }

        /// <summary>Absolute day when episode was marked ready for review (timely review bonus).</summary>
        public int ReviewOfferedAbsoluteDay { get; set; }

        /// <summary>Level 1: 0.95 stress gain multiplier after successful assignment.</summary>
        public int AssignmentBoostDaysRemaining { get; set; }

        public bool SafePersonUnlocked { get; set; }

        public bool ForestRescueUnlocked { get; set; }

        public bool GroundingDialogueUnlocked { get; set; }

        /// <summary>Episode id, за который уже начислен ambient trust.</summary>
        public string? LastAmbientTrustEpisodeId { get; set; }

        public bool SupportiveTalkTrustToday { get; set; }

        /// <summary>Ключи trust-реплик из stress_flow_dialogues.json, уже показанные один раз.</summary>
        public HashSet<string> ShownCareTrustDialogueKeys { get; set; } = new();

        /// <summary>«Границы»: ответы на вопросы Харви (HarveyCareConsents → ответ).</summary>
        public Dictionary<string, HarveyConsentAnswer> Consents { get; set; } = new();

        /// <summary>Абсолютный день, когда Харви последний раз спрашивал о каждом разрешении.</summary>
        public Dictionary<string, int> ConsentAskedDay { get; set; } = new();

        /// <summary>Разрешения, за ответ на которые доверие уже начислено (один раз на вопрос).</summary>
        public HashSet<string> ConsentTrustAwarded { get; set; } = new();

        /// <summary>Абсолютный день последнего вопроса (не больше одного вопроса в день).</summary>
        public int LastConsentAskDay { get; set; }

        /// <summary>Сколько очков можно вернуть разговором после штрафа (0 — разговор не нужен).</summary>
        public int PendingRepairPoints { get; set; }

        /// <summary>Причина последнего штрафа (HarveyCareTrustReasons) — для реплики Харви.</summary>
        public string? PendingRepairReason { get; set; }

        /// <summary>Абсолютный день штрафа; разговор о нём доступен ConsentRepairWindowDays дней.</summary>
        public int PendingRepairDay { get; set; }
    }
}
