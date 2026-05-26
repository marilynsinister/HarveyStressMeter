namespace HarveyStressMeter.Constants
{
    public static class FlashbackRescueEventIds
    {
        public const string MidTrust = "HarveyStress_GotoroForestRescue_MidTrust";
        public const string HighTrust = "HarveyStress_GotoroForestRescue_HighTrust";
        public const string Dating = "HarveyStress_GotoroForestRescue_Dating";
        public const string Married = "HarveyStress_GotoroForestRescue_Married";

        public static string ForTier(string tier) => tier switch
        {
            FlashbackRescueTiers.MidTrust => MidTrust,
            FlashbackRescueTiers.HighTrust => HighTrust,
            FlashbackRescueTiers.Dating => Dating,
            FlashbackRescueTiers.Married => Married,
            _ => MidTrust,
        };
    }
}
