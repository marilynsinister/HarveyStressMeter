using System.Text;
using System.Text.Json;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    internal static class McpSocialExposureTools
    {
        public static string SocialGet(SocialExposureService service)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine(service.BuildDebugSnapshot());
            return sb.ToString().TrimEnd();
        }

        public static string SocialSet(SocialExposureService service, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "value", out var value))
                return "Error: value is required (0–100).";

            if (value < 0 || value > 100)
                return "Error: value must be 0–100.";

            service.SetExposure(value);
            return SocialGet(service);
        }

        public static string SocialAdd(SocialExposureService service, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "amount", out var amount))
                return "Error: amount is required.";

            service.AddExposure(amount, "mcp stress_social_add");
            return SocialGet(service);
        }

        public static string SocialReset(SocialExposureService service)
        {
            service.ResetDaily();
            return SocialGet(service);
        }

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            return el.TryGetInt32(out value);
        }
    }
}
