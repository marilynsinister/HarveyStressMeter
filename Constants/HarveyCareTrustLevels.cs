namespace HarveyStressMeter.Constants
{
    public static class HarveyCareTrustLevels
    {
        public const int NoTrustBonus = 0;
        public const int FamiliarDoctor = 1;
        public const int TrustedDoctor = 2;
        public const int SafePerson = 3;
        public const int Anchor = 4;

        public static string GetDisplayName(int level) => level switch
        {
            FamiliarDoctor => "FamiliarDoctor",
            TrustedDoctor => "TrustedDoctor",
            SafePerson => "SafePerson",
            Anchor => "Anchor",
            _ => "NoTrustBonus",
        };
    }

    public static class HarveyCareTrustReasons
    {
        public const string AmbientRecommendation = "AmbientRecommendation";
        public const string TreatmentEpisodeComplete = "TreatmentEpisodeComplete";
        public const string GotoroEpisodeComplete = "GotoroEpisodeComplete";
        public const string TimelyReview = "TimelyReview";
        public const string SafeLocationStabilized = "SafeLocationStabilized";
        public const string ForestRescue = "ForestRescue";
        public const string SupportiveTalk = "SupportiveTalk";
        public const string LongIgnoredAssignment = "LongIgnoredAssignment";
    }
}
