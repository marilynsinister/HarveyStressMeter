namespace HarveyStressMeter.Constants
{
    /// <summary>
    /// Общие формулировки для квестов-назначений Харви (стресс, не injury).
    /// </summary>
    public static class StressQuestCopy
    {
        /// <summary>Строка в описании квеста до выполнения условий.</summary>
        public const string TalkToHarveyWhenDone = "Когда выполните назначение, поговорите с Харви.";

        /// <summary>Цель в журнале после выполнения условий (динамически через UpdateQuest).</summary>
        public const string ReadyForReviewObjective = "Поговорить с Харви.";

        public const string ReadyForReviewHud = "Назначение выполнено. Поговорите с Харви.";

        public const string TreatmentAssignedHud = "Харви оставил назначение.";

        public const string ReviewDialogue =
            "Спасибо, что выполнила назначение. Расскажи, как ты себя чувствуешь сейчас — я проверю, всё ли в порядке.";

        public static string AssignmentDescription(string reason)
            => $"Харви, как ваш врач, выдал медицинское назначение: {reason} {TalkToHarveyWhenDone}";
    }
}
