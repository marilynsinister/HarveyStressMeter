namespace HarveyStressMeter.Models
{
    /// <summary>Дневная шкала социальной усталости (0–100) и флаги пороговых HUD за сегодня.</summary>
    public sealed class SocialExposureState
    {
        /// <summary>Накопленная социальная усталость за текущий день (0–100).</summary>
        public int SocialExposureToday { get; set; }

        /// <summary>Битовая маска показанных пороговых HUD: 40=1, 70=2, 90=4.</summary>
        public int ThresholdsShownToday { get; set; }
    }

}
