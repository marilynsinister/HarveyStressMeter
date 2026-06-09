using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    public sealed class StressMessagesRoot
    {
        public StressMessageSettings Settings { get; set; } = new();
        public LightningFrightMessagePools LightningFright { get; set; } = new();
    }

    public sealed class StressMessageSettings
    {
        public int HudMessageCooldownMinMinutes { get; set; } = 30;
        public int HudMessageCooldownMaxMinutes { get; set; } = 60;
    }

    public sealed class LightningFrightMessagePools
    {
        public List<string> Triggered { get; set; } = new();
        public List<string> MovingToForest { get; set; } = new();
        public List<string> InForest { get; set; } = new();
        public List<string> Stabilized { get; set; } = new();
        public List<string> ReturnedTooEarly { get; set; } = new();
        public List<string> AfterHarveyStabilized { get; set; } = new();
        public List<string> LeavingHarveyAnchor { get; set; } = new();
        public List<string> RelapseWarning { get; set; } = new();
        public List<string> RelapseTriggered { get; set; } = new();
        public List<string> RelapseSuppressedNearHarvey { get; set; } = new();
    }
}
