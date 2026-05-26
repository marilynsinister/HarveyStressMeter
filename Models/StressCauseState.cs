namespace HarveyStressMeter.Models
{
    /// <summary>Состояние одной активной причины стресса.</summary>
    public sealed class StressCauseState
    {
        public string CauseId { get; set; } = "";
        public string SourceBuffId { get; set; } = "";
        public int Weight { get; set; }
        public bool IsActive { get; set; }
        public bool IsSevere { get; set; }
        public int AppliedTime { get; set; }
        public int LastUpdatedTime { get; set; }
        public bool CanSelfResolve { get; set; }
        public bool RequiresHarveyIfSevere { get; set; }
    }
}
