using System;
using System.Collections.Generic;
using System.Linq;
using HarveyOverhaul.Core.Models;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewValley;

namespace HarveyStressMeter.Api;

/// <summary>
/// Две секции окна H, чтобы стресс не казался случайным наказанием:
/// «Что влияет на шкалу» (вкладка «Стресс») — то же, что подсказка над шкалой, но всегда под рукой;
/// «Прогноз» (вкладка «Обзор») — что ждёт завтра, как дела у Харви сегодня и сколько до «Хорошей недели».
/// </summary>
internal static class StressPanelForecast
{
    private const int MaxLines = 8;
    private const int CalmDaysRequired = 5;

    public static HarveyPanelSectionDto? BuildBreakdown(StressLoadService stressLoad)
    {
        var contributions = stressLoad.GetContributions();
        if (contributions.Count == 0)
            return null;

        var lines = contributions
            .Take(MaxLines)
            .Select(c => $"{(c.Weight > 0 ? "+" : "−")}{Math.Abs(c.Weight)}  {c.Label}")
            .ToList();
        if (contributions.Count > MaxLines)
            lines.Add($"… ещё {contributions.Count - MaxLines}");

        return new HarveyPanelSectionDto
        {
            Title = "Что влияет на шкалу",
            Body = string.Join("\n", lines),
            Status = $"Сейчас: {stressLoad.GetCurrentStressLoad()} из {stressLoad.GetMaxStressLoad()}. Плюс — давит, минус — помогает.",
            Priority = 5,
            Severity = HarveyPanelSeverity.Info,
        };
    }

    public static HarveyPanelSectionDto? BuildForecast(StressLoadState state)
    {
        var lines = new List<string>();

        string? weather = Game1.weatherForTomorrow switch
        {
            Game1.weather_lightning => "Завтра гроза. Держи под рукой план на грозу: дом или клиника, и Харви рядом.",
            Game1.weather_rain => "Завтра дождь: на улице стресс копится чуть быстрее. Хороший день для дел под крышей.",
            Game1.weather_snow => "Завтра снег: одевайся теплее, на холоде копится усталость.",
            Game1.weather_green_rain => "Завтра зелёный дождь: ферма зарастёт, но спешить не обязательно.",
            _ => null,
        };
        if (weather != null)
            lines.Add(weather);

        if (Utility.isFestivalDay(Game1.dayOfMonth + 1, Game1.season))
            lines.Add("Завтра праздник: будет людно. Если станет тяжело — можно уйти пораньше, это нормально.");

        var player = Game1.player;
        if (player.mailReceived.Contains("HarveyState_Sick"))
            lines.Add("Харви сегодня простыл. Горячее блюдо или чай — и ему станет легче.");
        else if (player.mailReceived.Contains("HarveyState_Tired"))
            lines.Add("Харви сегодня устал после ночного вызова. Зайди поговорить или принеси кофе.");
        else if (player.mailReceived.Contains("HarveyState_Worried"))
            lines.Add("Харви тревожится после вчерашнего. Покажись ему — этого достаточно.");

        int calm = state.CalmDayHistory.Sum();
        int recorded = state.CalmDayHistory.Count;
        string goodWeek = calm >= CalmDaysRequired && recorded >= 7
            ? "Хорошая неделя набрана — Харви скоро напишет."
            : $"Спокойных дней за неделю: {calm} из {Math.Max(recorded, 1)} (для письма Харви нужно {CalmDaysRequired} из 7).";
        if (state.GoodWeekStreak >= 2)
            goodWeek += $" Хороших недель подряд: {state.GoodWeekStreak}.";
        lines.Add(goodWeek);

        return new HarveyPanelSectionDto
        {
            Title = "Прогноз",
            Body = string.Join("\n", lines),
            Priority = 80,
            Severity = HarveyPanelSeverity.Info,
        };
    }
}
