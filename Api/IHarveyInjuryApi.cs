using System.Collections.Generic;

namespace HarveyStressMeter.Api
{
    /// <summary>
    /// Контракт read-only API мода HarveyOverhaulInjury.
    /// Должен совпадать по сигнатурам с HarveyOverhaul.InjuryCare.Api.IHarveyInjuryApi.
    /// </summary>
    public interface IHarveyInjuryApi
    {
        bool IsAvailable { get; }

        InjuryPanelStateDto GetPanelState();

        RecoveryPlanPanelDto GetRecoveryPlanState();
    }

    public sealed class RecoveryPlanPanelDto
    {
        public bool HasPlan { get; set; }

        public string Title { get; set; } = "";

        public string BodyText { get; set; } = "";

        public string SummaryLine { get; set; } = "";
    }

    public sealed class InjuryPanelStateDto
    {
        public bool HasAnyInjury { get; set; }

        public List<InjuryPanelItemDto> Injuries { get; set; } = new();

        public List<InjuryPanelItemDto> Complications { get; set; } = new();

        public string SummaryText { get; set; } = "";
    }

    public sealed class InjuryPanelItemDto
    {
        public string BuffId { get; set; } = "";

        public string Title { get; set; } = "";

        public string StatusText { get; set; } = "";

        public string AdviceText { get; set; } = "";

        public int CurrentPhase { get; set; }

        public int TotalPhases { get; set; }

        public bool TreatmentStarted { get; set; }

        public bool ReadyForNextPhase { get; set; }

        public bool ReadyForRecovery { get; set; }

        public bool IsComplication { get; set; }
    }
}
