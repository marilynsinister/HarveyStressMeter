using System;
using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    public enum EpisodeSelectionAction
    {
        None,
        AmbientOnly,
        AwaitingReview,
        ReminderOnly,
        StartEpisode,
    }

    /// <summary>Результат выбора episode по правилам StressLoad.</summary>
    public sealed class EpisodeSelectionResult
    {
        public EpisodeSelectionAction Action { get; init; } = EpisodeSelectionAction.None;
        public string? EpisodeId { get; init; }
        public string? PrimaryBuffId { get; init; }
        public string? DisplayName { get; init; }
        public IReadOnlyList<string> MatchingEpisodeIds { get; init; } = Array.Empty<string>();
        public string? Reason { get; init; }
    }
}
