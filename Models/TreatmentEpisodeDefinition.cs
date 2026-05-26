using System.Collections.Generic;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Models
{
    /// <summary>Определение эпизода лечения — единое назначение Харви по общему состоянию.</summary>
    public sealed class TreatmentEpisodeDefinition
    {
        public string EpisodeId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public List<string> RelatedCauses { get; init; } = new();
        public int MinStressLoad { get; init; }
        public StressSeverity MinSeverity { get; init; } = StressSeverity.Calm;
        public bool RequiresHarveyTreatment { get; init; } = true;
        public bool IsEmergency { get; init; }
        public int Priority { get; init; }
        public string QuestId { get; init; } = "";
        public string StartDialogueKey { get; init; } = "";
        public string ReminderDialogueKey { get; init; } = "";
        public string ReadyForReviewDialogueKey { get; init; } = "";
        public string CompletionDialogueKey { get; init; } = "";
        public List<string> Objectives { get; init; } = new();

        /// <summary>Fallback buffId для совместимости с существующим progress/trigger pipeline.</summary>
        public string DefaultPrimaryBuffId { get; init; } = "";
    }
}
