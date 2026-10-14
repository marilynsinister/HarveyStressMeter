using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Второй триггер прошлого после грома — огонь. В T1 фермерша рассказывает Харви про склад и дым
    /// (флаг <see cref="FireKnownFlag"/>). После этого запах дыма у открытого огня (костёр, жаровня,
    /// растопленный камин, работающая печь) даёт лёгкий стресс — не чаще раза в день, — а Харви
    /// при следующем разговоре замечает это (тема <see cref="HarveyTopic"/>).
    /// До рассказа и с выключенным TraumaBackstory флага нет — механика молчит.
    /// </summary>
    public sealed class FireMemoryService
    {
        public const string FireKnownFlag = "HarveyBackstory_FireKnown";
        public const string HarveyTopic = "HarveyScars_FireReaction";

        /// <summary>Клеток до огня, на которых «пахнет дымом».</summary>
        private const int Radius = 2;

        /// <summary>Сколько игровых минут у огня, прежде чем память догонит.</summary>
        private const int MinutesNearFire = 20;

        private readonly ModConfig _config;
        private readonly BuffService _buffService;
        private readonly StressLoadService _stressLoadService;
        private readonly IMonitor _monitor;

        private int _minutesNear;
        private bool _triggeredToday;

        public FireMemoryService(ModConfig config, BuffService buffService, StressLoadService stressLoadService, IMonitor monitor)
        {
            _config = config;
            _buffService = buffService;
            _stressLoadService = stressLoadService;
            _monitor = monitor;
        }

        public void OnDayStarted()
        {
            _minutesNear = 0;
            _triggeredToday = false;
        }

        public void OnTimeChanged(int newTime)
        {
            if (!_config.EnableFireMemoryTrigger || _triggeredToday || !Context.IsWorldReady || Game1.eventUp)
                return;

            if (!Game1.player.mailReceived.Contains(FireKnownFlag))
                return;

            if (!IsNearOpenFire(Game1.currentLocation, Game1.player.TilePoint))
            {
                _minutesNear = 0;
                return;
            }

            _minutesNear += 10;
            if (_minutesNear < MinutesNearFire)
                return;

            _triggeredToday = true;

            // Длительность — из Data/Buffs (несколько игровых часов): когда бафф истечёт,
            // SyncFromGameState снимет и причину стресса. ApplyBuffFromData дал бы бафф на весь день.
            Game1.player.applyBuff(new Buff(BuffIds.FireMemory));
            if (!_buffService.HasBuff(BuffIds.FireMemory))
            {
                _monitor.Log("[FireMemory] Бафф не применился — нет buffStressFireMemory в Data/Buffs (TraumaBackstory выключен?).", LogLevel.Trace);
                return;
            }

            _stressLoadService.AddCauseFromBuff(BuffIds.FireMemory);
            Game1.player.activeDialogueEvents[HarveyTopic] = 2;
            Game1.addHUDMessage(new HUDMessage("Запах дыма. Руки сами тянутся закрыть голову… Отойди от огня и подыши.", HUDMessage.error_type));
            _monitor.Log($"[FireMemory] Сработало у огня в {Game1.currentLocation?.Name} ({newTime}).", LogLevel.Debug);
        }

        private static bool IsNearOpenFire(GameLocation? location, Point tile)
        {
            if (location == null)
                return false;

            for (int x = tile.X - Radius; x <= tile.X + Radius; x++)
            {
                for (int y = tile.Y - Radius; y <= tile.Y + Radius; y++)
                {
                    if (location.objects.TryGetValue(new Vector2(x, y), out var obj) && IsBurning(obj))
                        return true;
                }
            }

            foreach (var furniture in location.furniture)
            {
                if (!furniture.IsOn || !IsFireFurniture(furniture))
                    continue;

                var box = furniture.boundingBox.Value;
                int fx = box.X / Game1.tileSize, fy = box.Y / Game1.tileSize;
                if (Math.Abs(fx - tile.X) <= Radius + 1 && Math.Abs(fy - tile.Y) <= Radius + 1)
                    return true;
            }

            return false;
        }

        /// <summary>Костёр и жаровни (класс Torch — всегда горят, кроме обычных факелов), печь с загрузкой.</summary>
        private static bool IsBurning(StardewValley.Object obj)
        {
            if (obj is Torch && obj.Name != "Torch")
                return obj.IsOn;

            return obj.Name is "Furnace" or "Heavy Furnace" && obj.heldObject.Value != null && obj.MinutesUntilReady > 0;
        }

        private static bool IsFireFurniture(Furniture furniture)
            => furniture.Name.Contains("Fireplace", StringComparison.OrdinalIgnoreCase)
               || furniture.Name.Contains("Brazier", StringComparison.OrdinalIgnoreCase);
    }
}
