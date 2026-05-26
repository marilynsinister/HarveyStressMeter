namespace HarveyStressMeter.Constants
{
    public static class FlashbackRescueTiers
    {
        public const string MidTrust = "MidTrust";
        public const string HighTrust = "HighTrust";
        public const string Dating = "Dating";
        public const string Married = "Married";

        public static bool TryParse(string? value, out string tier)
        {
            tier = value?.Trim() ?? "";
            if (string.IsNullOrEmpty(tier))
                return false;

            if (string.Equals(tier, MidTrust, StringComparison.OrdinalIgnoreCase))
            {
                tier = MidTrust;
                return true;
            }

            if (string.Equals(tier, HighTrust, StringComparison.OrdinalIgnoreCase))
            {
                tier = HighTrust;
                return true;
            }

            if (string.Equals(tier, Dating, StringComparison.OrdinalIgnoreCase))
            {
                tier = Dating;
                return true;
            }

            if (string.Equals(tier, Married, StringComparison.OrdinalIgnoreCase))
            {
                tier = Married;
                return true;
            }

            return false;
        }
    }
}
