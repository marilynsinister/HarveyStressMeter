namespace HarveyStressMeter.Helpers
{
    /// <summary>Строки ремиссии и рецидива страха темноты.</summary>
    public static class DarknessRemissionCopy
    {
        public const string WarningRisk50 =
            "Темнота снова кажется слишком густой. Спокойный вечер дома поможет удержаться.";

        public const string WarningRisk75 =
            "Ремиссия ослабла. Лучше поговорить с Харви.";

        public const string RelapseHud =
            "Это не провал. Это откат. Такое бывает. Но я не позволю тебе делать вид, что ничего не происходит.";

        public const string RelapseTonightHud =
            "Сегодня без ночных прогулок. Дом. Свет. Один спокойный час.";

        public const string RelapseComeEarlyHud =
            "Если темнота снова стала тяжёлой — приходи ко мне раньше, а не когда уже трясёт.";

        public const string RemissionStartedHud =
            "Ремиссия началась. Семь дней я не допущу немедленного отката — но береги себя ночью.";

        public const string ProphylaxisOfferHud =
            "Поговорили — хорошо. Сегодня один спокойный вечер дома при свете. Без геройства.";

        public static string RiskStageLabel(int risk) => risk switch
        {
            >= 100 => "рецидив",
            >= 75 => "на грани рецидива",
            >= 50 => "ремиссия ослабла",
            >= 25 => "темнота снова тревожит",
            _ => "стабильно",
        };

        public static string ProphylaxisObjective(int minutesToday)
            => "Профилактика: один спокойный вечер дома при свете после 20:00.\n" +
               $"Сегодня: {minutesToday}/60 минут.\n" +
               "Риск высокий — полное лечение пока не начинаем.";

        public static string ProphylaxisObjectiveDone()
            => "Профилактический вечер зачтён. Риск снижен. Продолжай беречь себя ночью.";
    }
}
