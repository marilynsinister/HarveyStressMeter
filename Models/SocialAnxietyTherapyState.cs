using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Сохраняемое состояние терапии социальной тревожности (отдельно от vanilla dialogue keys).
    /// </summary>
    public sealed class SocialAnxietyTherapyState
    {
        public SocialAnxietyTherapyPhase Phase { get; set; } = SocialAnxietyTherapyPhase.None;

        /// <summary>День, когда таймер достиг 60/60 (для отладки и персистентности).</summary>
        public SDate? TimerCompletedOn { get; set; }

        /// <summary>День, когда все условия квеста выполнены и ждём follow-up с Харви.</summary>
        public SDate? ReadyToCompleteOn { get; set; }

        /// <summary>Зафиксированный прогресс таймера на момент TimerCompleted (не сбрасывается при отходе от Харви).</summary>
        public int TimerSecondsAtCompletion { get; set; }

        /// <summary>Какой путь завершения активен: path1 (3+60) или path2 (5 разговоров).</summary>
        public string CompletionPath { get; set; } = "";
    }
}
