using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    /// <summary>Активное назначение Харви по TreatmentEpisode (единый квест на несколько causes).</summary>
    public sealed class TreatmentEpisodeState
    {
        public string EpisodeId { get; set; } = "";
        public List<string> RelatedCauseIds { get; set; } = new();
        public string QuestId { get; set; } = "";
        public bool TreatmentStarted { get; set; }
        public bool ObjectivesCompleted { get; set; }
        public bool AwaitingHarveyReview { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCured { get; set; }
        public int StartedTime { get; set; }
        public int? ReadyForReviewTime { get; set; }
        public string? PrimaryCauseId { get; set; }

        public bool IsActiveEpisode() =>
            TreatmentStarted && !IsCompleted && !IsCured;
    }
}
