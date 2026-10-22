using System.Linq;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// «Хорошая неделя»: если 5 из последних 7 дней закончились спокойно (шкала ниже «Напряжения»),
    /// Харви присылает письмо с похвалой и подарком. Мягкая награда за заботу о себе — без штрафов.
    /// Серии: вторая неделя подряд — письмо побогаче, каждая четвёртая («месяц») — письмо, тема
    /// <see cref="GoodMonthTopic"/> на неделю и пикник с Харви (CP: goodWeek.json, сцена повторяется).
    /// Пропущенная неделя просто начинает серию заново.
    /// </summary>
    public sealed class GoodWeekService
    {
        private const int WindowDays = 7;
        private const int CalmDaysRequired = 5;
        private const string MailPrefix = "marilynsinister.HarveyOverhaul_mailGoodWeek";
        public const string GoodMonthTopic = "HarveyGoodMonth";
        public const string PicnicEventId = "HarveyOverhaulGoodWeek.GW1_Picnic";

        /// <summary>Следующая хорошая неделя считается продолжением серии, если пришла не позже этого срока.</summary>
        private const int StreakGraceDays = WindowDays + 3;

        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly ModConfig _config;
        private readonly IMonitor _monitor;

        public GoodWeekService(SaveData data, StressLoadService stressLoadService, ModConfig config, IMonitor monitor)
        {
            _data = data;
            _stressLoadService = stressLoadService;
            _config = config;
            _monitor = monitor;
        }

        public void OnDayEnding()
        {
            var history = _data.StressLoad.CalmDayHistory;
            bool calm = _stressLoadService.GetCurrentStressLoad() < _config.MildThreshold;
            history.Add(calm ? 1 : 0);
            while (history.Count > WindowDays)
                history.RemoveAt(0);
        }

        public void OnDayStarted()
        {
            var state = _data.StressLoad;
            int today = (int)Game1.stats.DaysPlayed;
            if (state.CalmDayHistory.Count < WindowDays || today - state.LastGoodWeekRewardDay < WindowDays)
                return;

            int calmDays = state.CalmDayHistory.Sum();
            if (calmDays < CalmDaysRequired)
                return;

            state.GoodWeekStreak = today - state.LastGoodWeekRewardDay <= StreakGraceDays
                ? state.GoodWeekStreak + 1
                : 1;

            string tier = state.GoodWeekStreak % 4 == 0 ? "Month" : state.GoodWeekStreak == 2 ? "Streak" : "";
            string mailId = MailPrefix + tier + ResolveVariant();
            Game1.player.mailbox.Add(mailId);
            state.LastGoodWeekRewardDay = today;
            state.CalmDayHistory.Clear();

            if (tier == "Month")
            {
                // Пикник повторяется: каждый «месяц» спокойствия — снова.
                Game1.player.eventsSeen.Remove(PicnicEventId);
                Game1.player.activeDialogueEvents[GoodMonthTopic] = 7;
            }

            _monitor.Log($"[GoodWeek] {calmDays}/{WindowDays} спокойных дней, серия {state.GoodWeekStreak} — письмо {mailId}", LogLevel.Info);
        }

        private static string ResolveVariant()
        {
            if (!Game1.player.friendshipData.TryGetValue("Harvey", out var friendship))
                return "_Friend";

            if (friendship.IsMarried())
                return "_Married";

            return friendship.IsDating() ? "_Dating" : "_Friend";
        }
    }
}
