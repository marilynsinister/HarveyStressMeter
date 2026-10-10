using System;
using System.Collections.Generic;
using System.Linq;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Minigames;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// «Занятия для души»: обычные дела фермы немного снижают StressLoad.
    /// Каждое следующее срабатывание за день слабее предыдущего, общий дневной потолок — ComfortDailyReliefCap.
    /// </summary>
    public sealed class ComfortActivityService
    {
        private const int PollIntervalTicks = 60;
        private const int DwellMinutesRequired = 30;

        private sealed record Activity(string Id, int BaseRelief, int MaxUsesPerDay, string Message);

        private static class Ids
        {
            public const string Fishing = "Fishing";
            public const string Foraging = "Foraging";
            public const string FarmAnimal = "FarmAnimal";
            public const string Pet = "Pet";
            public const string Sunset = "Sunset";
            public const string Saloon = "Saloon";
            public const string Arcade = "Arcade";
        }

        private static readonly Dictionary<string, Activity> Activities = new[]
        {
            new Activity(Ids.Fishing, 4, 4, "Плеск воды и поплавок. Мысли наконец замолкают."),
            new Activity(Ids.Foraging, 2, 4, "Тихий сбор в лесу успокаивает."),
            new Activity(Ids.FarmAnimal, 2, 5, "Тёплый бок животного под ладонью — и на душе теплее."),
            new Activity(Ids.Pet, 5, 1, "Питомец рад тебе. Это лучшее лекарство."),
            new Activity(Ids.Sunset, 8, 1, "Ты смотришь на закат. Дышать становится легче."),
            new Activity(Ids.Saloon, 6, 1, "Шум салуна, тёплый свет. Можно просто посидеть."),
            new Activity(Ids.Arcade, 5, 1, "Пара раундов в автомате — и голова проясняется."),
        }.ToDictionary(a => a.Id);

        private static readonly HashSet<string> SunsetLocations = new(StringComparer.OrdinalIgnoreCase)
        {
            "Beach", "Mountain", "Forest", "Farm", "Summit", "IslandWest", "IslandSouth",
        };

        private readonly SaveData _data;
        private readonly ModConfig _config;
        private readonly StressLoadService _stressLoadService;
        private readonly IMonitor _monitor;

        public ComfortActivityService(SaveData data, ModConfig config, StressLoadService stressLoadService, IMonitor monitor)
        {
            _data = data;
            _config = config;
            _stressLoadService = stressLoadService;
            _monitor = monitor;
        }

        private ComfortActivityState State => _data.ComfortActivities;

        public void OnUpdateTicked(int ticks)
        {
            if (!_config.EnableComfortActivities || !Context.IsWorldReady || ticks % PollIntervalTicks != 0)
                return;

            EnsureToday();
            var stats = Game1.player.stats;

            uint fish = stats.FishCaught;
            if (fish > State.LastFishCaught)
                TryRelieve(Ids.Fishing);
            State.LastFishCaught = fish;

            uint foraged = stats.ItemsForaged;
            if (foraged > State.LastItemsForaged)
                TryRelieve(Ids.Foraging);
            State.LastItemsForaged = foraged;

            if (Game1.currentMinigame is AbigailGame or MineCart)
                TryRelieve(Ids.Arcade);

            CheckAnimalsAndPets(Game1.currentLocation);
        }

        public void OnTimeChanged(int oldTime, int newTime)
        {
            if (!_config.EnableComfortActivities || !Context.IsWorldReady)
                return;

            EnsureToday();
            var location = Game1.currentLocation;
            if (location == null || Game1.CurrentEvent != null)
                return;

            int minutes = Math.Max(0, ToMinutes(newTime) - ToMinutes(oldTime));

            bool sunsetHour = newTime >= 1800 && newTime <= 2000;
            if (sunsetHour && location.IsOutdoors && !location.IsRainingHere() && SunsetLocations.Contains(location.Name))
                Dwell(Ids.Sunset, minutes);

            if (newTime >= 1700 && location.Name == "Saloon")
                Dwell(Ids.Saloon, minutes);
        }

        public void OnDayStarted()
        {
            EnsureToday();
        }

        private void CheckAnimalsAndPets(GameLocation? location)
        {
            if (location == null)
                return;

            string key = location.NameOrUniqueName;
            int pettedHere = location.animals.Values.Count(a => a.wasPet.Value);
            int seenBefore = State.PettedAnimalsSeen.GetValueOrDefault(key, -1);
            if (seenBefore >= 0 && pettedHere > seenBefore)
                TryRelieve(Ids.FarmAnimal);
            State.PettedAnimalsSeen[key] = pettedHere;

            int today = Game1.Date.TotalDays;
            long playerId = Game1.player.UniqueMultiplayerID;
            bool pettedPet = location.characters.OfType<Pet>()
                .Any(p => p.lastPetDay.TryGetValue(playerId, out int day) && day == today);
            if (pettedPet)
                TryRelieve(Ids.Pet);
        }

        private void Dwell(string activityId, int minutes)
        {
            if (minutes <= 0)
                return;

            int total = State.DwellMinutesToday.GetValueOrDefault(activityId) + minutes;
            State.DwellMinutesToday[activityId] = total;
            if (total >= DwellMinutesRequired)
                TryRelieve(activityId);
        }

        private void TryRelieve(string activityId)
        {
            var activity = Activities[activityId];
            int uses = State.UsesToday.GetValueOrDefault(activityId);
            if (uses >= activity.MaxUsesPerDay)
                return;

            int load = _stressLoadService.GetCurrentStressLoad();
            int capLeft = Math.Max(0, _config.ComfortDailyReliefCap - State.ReliefToday);
            if (load <= 0 || capLeft <= 0)
                return;

            // Убывающая отдача: каждое следующее срабатывание за день на 1 слабее, минимум 1.
            int relief = Math.Min(capLeft, Math.Max(1, activity.BaseRelief - uses));
            State.UsesToday[activityId] = uses + 1;
            State.ReliefToday += relief;
            _stressLoadService.DecayStress(relief);

            if (uses == 0)
                Game1.addHUDMessage(new HUDMessage(activity.Message, HUDMessage.newQuest_type));

            _monitor.Log(
                $"[Comfort] {activityId} #{uses + 1}: StressLoad -{relief} → {_stressLoadService.GetCurrentStressLoad()} " +
                $"(день {State.ReliefToday}/{_config.ComfortDailyReliefCap})",
                LogLevel.Debug);
        }

        private void EnsureToday()
        {
            int today = (int)Game1.stats.DaysPlayed;
            if (State.Day == today)
                return;

            State.Day = today;
            State.UsesToday.Clear();
            State.DwellMinutesToday.Clear();
            State.PettedAnimalsSeen.Clear();
            State.ReliefToday = 0;
            State.LastFishCaught = Game1.player.stats.FishCaught;
            State.LastItemsForaged = Game1.player.stats.ItemsForaged;
        }

        private static int ToMinutes(int time) => time / 100 * 60 + time % 100;
    }
}
