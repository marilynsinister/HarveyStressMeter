using HarveyStressMeter.Models;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Единые подписи шага stress treatment для debug/HUD/MCP.
    /// </summary>
    public static class TreatmentNextStep
    {
        public const string Prescription = "Talk to Harvey for prescription";
        public const string Objective = "Complete assignment objective";
        public const string Review = "Talk to Harvey for review";
        public const string Completed = "Completed/Cured";

        public static string Resolve(TreatmentState? treatment, bool hasBuffInGame)
        {
            if (treatment != null && (treatment.IsCured || treatment.IsCompleted))
                return Completed;

            if (treatment?.AwaitingHarveyReview == true)
                return Review;

            if (treatment?.TreatmentStarted == true)
                return Objective;

            if (hasBuffInGame)
                return Prescription;

            if (treatment != null && !treatment.TreatmentStarted)
                return Prescription;

            return "(no active stress flow)";
        }
    }
}
