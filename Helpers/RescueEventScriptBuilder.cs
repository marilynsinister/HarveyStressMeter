using System.Text;
using HarveyStressMeter.Constants;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Fallback event script (CP — основной источник диалогов).</summary>
    public static class RescueEventScriptBuilder
    {
        public static string Build(string tier, string eventId)
        {
            var lines = GetTierLines(tier);
            var sb = new StringBuilder();
            sb.AppendLine("rain/");
            sb.AppendLine("-1000 -1000/");
            sb.AppendLine("farmer -1 -1 2 Harvey 1000 1000 3/");
            sb.AppendLine("skippable/");
            sb.AppendLine("ignoreCollisions Harvey/");
            sb.AppendLine("playSound thunder_small/");
            sb.AppendLine("emote farmer 28/");
            sb.AppendLine("pause 800/");
            sb.AppendLine("playSound doorOpen/");
            sb.AppendLine("faceDirection Harvey 3/");
            sb.AppendLine("pause 500/");
            sb.AppendLine("emote Harvey 16/");

            foreach (var speak in lines)
                sb.AppendLine($"speak Harvey \"{Escape(speak)}\"/");

            sb.AppendLine("pause 400/");
            sb.AppendLine($"end dialogue Harvey \"{Escape(GetClosingLine(tier))}\"");

            return sb.ToString().TrimEnd();
        }

        private static string[] GetTierLines(string tier) => tier switch
        {
            FlashbackRescueTiers.Married =>
            [
                "Любимая, посмотри на меня.$8",
                "Это гроза. Не Готоро.$8#$b#Ты в лесу за фермой. Я нашёл тебя.$8",
                "Сейчас мы не будем бежать.$8#$b#Досчитаем до пяти. Ты в Долине. Это дождь. Это гром.$l",
                "Назови три вещи, которые видишь. Сейчас опасности нет.$l",
                "Хорошо. Я никуда не уйду.$l",
            ],
            FlashbackRescueTiers.Dating =>
            [
                "@, это я. Харви.$8",
                "Ты в Долине. Я рядом. Можно я подойду ближе?$8",
                "Не надо вставать. Дыши со мной. Вдох... и выдох...$8",
                "Посмотри на меня. Гром далеко. Дождь здесь. Сейчас опасности нет.$u",
                "Я здесь. Я не буду тянуть тебя за руку.$l",
            ],
            FlashbackRescueTiers.HighTrust =>
            [
                "@... я понял, где тебя искать.$8",
                "Не спорь с телом. Оно испугалось раньше, чем ты успела подумать.$8",
                "Ты сейчас не там, где кажется?$8#$b#Посмотри на деревья. Дождь по листьям. Это здесь.$u",
                "Назови три вещи, которые видишь. Гром далеко. Сейчас опасности нет.$u",
                "Хорошо. Я рядом. Никуда не ухожу.$l",
            ],
            _ =>
            [
                "@? Это Харви. Я не подойду ближе, если вы не хотите.$8",
                "Я видел, как вы ушли из города после грома.$8#$b#Вы ранены?",
                "Вы в лесу. Идёт дождь. Это гром. Сейчас рядом нет взрывов.$u",
                "Посмотрите на меня. Назовите три вещи, которые видите.$u",
                "Дыхание — медленно. Я подожду.$l",
            ],
        };

        private static string GetClosingLine(string tier) => tier switch
        {
            FlashbackRescueTiers.Married =>
                "Когда будешь готова — поговорим дома. Сейчас просто верни себе землю под ногами.",
            FlashbackRescueTiers.Dating =>
                "Я подожду столько, сколько нужно. Когда будешь готова — скажи.",
            FlashbackRescueTiers.HighTrust =>
                "Когда отпустит — зайди ко мне. Мы разберёмся без спешки.",
            _ => "Когда будете готовы — зайдите в клинику. Сейчас вам не нужно ничего решать.",
        };

        private static string Escape(string text)
            => text.Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
