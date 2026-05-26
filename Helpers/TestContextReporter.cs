using System.Text;
using StardewValley;
using StardewValley.Menus;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Read-only snapshot игрового контекста для MCP / hs.test.* QA.</summary>
    public static class TestContextReporter
    {
        public static string BuildReport()
        {
            var sb = new StringBuilder();
            void L(string line) => sb.AppendLine(line);

            var eventActive = GameStateHelper.IsEventActive();
            var eventId = GameStateHelper.GetCurrentEventId();
            var menuType = Game1.activeClickableMenu?.GetType().Name ?? "null";
            var speaker = Game1.currentSpeaker?.Name ?? "null";
            var location = Game1.currentLocation?.NameOrUniqueName ?? "null";

            StressDialoguePipelineGuard.CanRun(
                out var guardReason,
                requireDialogueBox: false,
                requireHarveySpeaker: false);

            var dialogue = HarveyDevTalkHelper.GetDialogueBoxSnapshot();

            L("[TestContext]");
            L($"EventActive={eventActive}, EventUp={Game1.eventUp}, EventId={eventId ?? "null"}");
            L($"Menu={menuType}, Speaker={speaker}, Location={location}, Fade={Game1.fadeToBlack}");
            L($"StressPipelineGuard={(guardReason == StressDialoguePipelineGuard.BlockReason.None ? "OK" : guardReason)}");
            L($"DialogueBox={dialogue.HasDialogueBox}, IsQuestion={dialogue.IsQuestion}, ResponseCount={dialogue.Responses.Count}");

            if (dialogue.Responses.Count > 0)
            {
                L("Responses:");
                foreach (var r in dialogue.Responses)
                    L($"  [{r.Index}] {r.ResponseKey}: {r.ResponseText}");
            }

            if (eventActive)
            {
                L("Hint: event/script active — stress dialogue tools are blocked. Poll until EventActive=false.");
            }
            else if (dialogue.IsQuestion)
            {
                L("Hint: DialogueBox has #$y responses — use stress_list_responses / stress_choose_response (not required for stress start dialogue; auto-start has no consent question).");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
