using System.Linq;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// «Хорошая неделя»: если 5 из последних 7 дней закончились спокойно (шкала ниже «Напряжения»),
    /// Харви присылает письмо с похвалой и подарком. Мягкая награда за заботу о себе — без штрафов.
    /// </summary>
    public sealed class GoodWeekService
    {
        private const int WindowDays = 7;
        private const int CalmDaysRequired = 5;
        private const string MailPrefix = "marilynsinister.HarveyOverhaul_mailGoodWeek";

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

            string mailId = MailPrefix + ResolveVariant();
            Game1.player.mailbox.Add(mailId);
            state.LastGoodWeekRewardDay = today;
            state.CalmDayHistory.Clear();
            _monitor.Log($"[GoodWeek] {calmDays}/{WindowDays} спокойных дней — письмо {mailId}", LogLevel.Info);
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
