using System.Text;
using HarveyStressMeter.Api;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.UI;
using StardewModdingAPI;

namespace HarveyStressMeter.Services
{
    /// <summary>Форматирование DTO травм для вкладки «Травмы» и обзора.</summary>
    internal static class HarveyPanelInjuryFormatter
    {
        public static string FormatOverviewLine(InjuryPanelStateDto? state)
        {
            if (state == null)
                return "";

            if (!state.HasAnyInjury)
                return "";

            if (state.Injuries.Count > 0 && NeedsHarveyTalk(state.Injuries[0]))
                return HarveyPanelTexts.Overview.HarveyWaitingHeadline;

            return HarveyPanelTexts.Overview.InjuryAttention;
        }

        public static string FormatOverviewDetail(InjuryPanelStateDto? state)
        {
            if (state == null || !state.HasAnyInjury)
                return "";

            if (state.Injuries.Count > 0)
            {
                var main = state.Injuries[0];
                if (!string.IsNullOrWhiteSpace(main.AdviceText))
                    return main.AdviceText;
            }

            return HarveyPanelTexts.Tone(
                HarveyPanelTexts.Overview.InjuryCareHint,
                HarveyPanelTexts.Overview.InjuryCareHintInformal);
        }

        public static string FormatInjuriesTab(InjuryPanelStateDto? state, bool injuryModLoaded, bool apiResolved)
        {
            if (!injuryModLoaded)
            {
                return $"""
                    {HarveyPanelTexts.Injuries.DataUnavailableTitle}

                    {HarveyPanelTexts.Injuries.DataUnavailableBody}
                    """.Trim();
            }

            if (!apiResolved || state == null)
            {
                return $"""
                    {HarveyPanelTexts.Injuries.DataUnavailableTitle}

                    {HarveyPanelTexts.Plan.InjuryDataUnavailableBody}
                    """.Trim();
            }

            if (!state.HasAnyInjury)
            {
                return $"""
                    {HarveyPanelTexts.Injuries.NoInjuriesTitle}

                    {HarveyPanelTexts.Injuries.NoInjuriesBody}
                    """.Trim();
            }

            var sb = new StringBuilder();
            sb.AppendLine(HarveyPanelTexts.Injuries.ActiveCareTitle);
            sb.AppendLine(HarveyPanelTexts.Injuries.ActiveCareBody);
            sb.AppendLine();

            if (state.Injuries.Count > 0)
            {
                sb.AppendLine(HarveyPanelTexts.Injuries.MainInjuryLabel);
                AppendItemBlock(sb, state.Injuries[0]);

                for (int i = 1; i < state.Injuries.Count; i++)
                {
                    sb.AppendLine();
                    sb.AppendLine(HarveyPanelTexts.Injuries.AdditionalLabel);
                    AppendItemBlock(sb, state.Injuries[i]);
                }
            }

            if (state.Complications.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(HarveyPanelTexts.Injuries.ComplicationsLabel);
                foreach (var complication in state.Complications)
                {
                    sb.AppendLine();
                    AppendItemBlock(sb, complication);
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static bool NeedsHarveyTalk(InjuryPanelItemDto item) =>
            item.ReadyForNextPhase || item.ReadyForRecovery;

        private static void AppendItemBlock(StringBuilder sb, InjuryPanelItemDto item)
        {
            sb.AppendLine(item.Title);

            if (!string.IsNullOrWhiteSpace(item.StatusText))
                sb.AppendLine(item.StatusText);

            if (!string.IsNullOrWhiteSpace(item.AdviceText))
                sb.AppendLine(item.AdviceText);
        }
    }
}
