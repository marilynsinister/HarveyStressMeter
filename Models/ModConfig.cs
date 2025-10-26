using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    public enum HandbookTab { Inventory, Skills }

    public sealed class ModConfig
    {
        public KeybindList OpenHandbook { get; set; } = KeybindList.Parse("LeftShift + H");
        public HandbookTab ButtonOn { get; set; } = HandbookTab.Inventory;
    }
}

