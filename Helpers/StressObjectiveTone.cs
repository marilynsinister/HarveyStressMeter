namespace HarveyStressMeter.Helpers
{
    /// <summary>Тон обращения к фермеру в текстах целей legacy-квестов (не реплики Харви).</summary>
    public static class StressObjectiveTone
    {
        public static bool UseInformalToFarmer()
            => HarveyFriendshipHelper.IsDatingHarvey() || HarveyFriendshipHelper.IsMarriedToHarvey();

        public static string TalkToHarvey()
            => UseInformalToFarmer() ? "Поговори с Харви." : "Поговорите с Харви.";
    }
}
