namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Фаза терапии социальной тревожности (квест HarveyMod_SocialRecovery).
    /// </summary>
    public enum SocialAnxietyTherapyPhase
    {
        None = 0,
        TimerActive = 1,
        TimerCompleted = 2,
        ReadyToComplete = 3,
        AwaitingHarveyFollowup = 4,
    }
}
