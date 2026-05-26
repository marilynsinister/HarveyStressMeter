namespace HarveyStressMeter.Models
{
    /// <summary>Состояние «рядом с Харви легче» (safe person aura).</summary>
    public sealed class HarveySafePersonAuraState
    {
        public int LastProcessTime { get; set; }

        public int LastMessageTime { get; set; }

        public int LastDecayAmount { get; set; }

        public float LastDistanceTiles { get; set; } = -1f;

        public bool LastHarveyNearby { get; set; }

        public bool LastSafeAuraActive { get; set; }
    }
}
