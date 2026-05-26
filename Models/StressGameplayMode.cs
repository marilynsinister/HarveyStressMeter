namespace HarveyStressMeter.Models
{
    /// <summary>Режим влияния стресса на gameplay.</summary>
    public enum StressGameplayMode
    {
        /// <summary>Сбалансированный: capped штрафы по severity.</summary>
        Balanced = 0,

        /// <summary>Story-first: без механических штрафов, только narrative/HUD.</summary>
        StoryFocus = 1,

        /// <summary>Усиленные capped штрафы (~+15%).</summary>
        Survival = 2,
    }
}
