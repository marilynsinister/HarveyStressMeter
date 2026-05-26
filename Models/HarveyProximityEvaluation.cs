namespace HarveyStressMeter.Models
{
    public sealed class HarveyProximityEvaluation
    {
        public bool AuraEnabled { get; init; } = true;

        public bool HarveyInSameLocation { get; init; }

        public float DistanceTiles { get; init; } = -1f;

        public float EffectiveMaxDistanceTiles { get; init; }

        public bool HarveyNearby { get; init; }

        public bool SafeAuraActive { get; init; }

        public string? BlockReason { get; init; }
    }
}
