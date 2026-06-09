using System.Text.Json;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Services;
using StardewModdingAPI;

namespace HarveyStressMeter.Testing
{
    internal static class McpSocialAnxietyTools
    {
        public static string SocialStart(
            SocialAnxietyTherapyService service,
            TreatmentService treatmentService,
            StateService stateService)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            if (!stateService.HasBuffInGame(BuffIds.Social))
                treatmentService.ApplyStressBuff(BuffIds.Social, "Социальный дискомфорт");

            treatmentService.StartTreatment(BuffIds.Social, QuestIds.Social, "Социальный дискомфорт");
            return service.BuildDebugSnapshot();
        }

        public static string SocialSetTimer(SocialAnxietyTherapyService service, JsonElement? arguments)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            if (!TryGetInt(arguments, "seconds", out var seconds)
                && !TryGetInt(arguments, "value", out seconds))
            {
                return "Error: seconds is required.";
            }

            service.DebugSetTimer(seconds);
            return service.BuildDebugSnapshot();
        }

        public static string SocialReady(SocialAnxietyTherapyService service)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            service.DebugForceReady();
            return service.BuildDebugSnapshot();
        }

        public static string SocialComplete(SocialAnxietyTherapyService service)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            service.DebugForceComplete();
            return service.BuildDebugSnapshot();
        }

        public static string SocialReset(SocialAnxietyTherapyService service)
        {
            service.Reset();
            return service.BuildDebugSnapshot();
        }

        public static string SocialDebugState(SocialAnxietyTherapyService service)
            => service.BuildDebugSnapshot();

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments?.TryGetProperty(name, out var el) != true)
                return false;

            return el.TryGetInt32(out value);
        }
    }
}
