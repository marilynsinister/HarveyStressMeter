using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Единая шкала StressLoad: собирает причины, пересчитывает нагрузку, severity и episode-кандидата.
    /// Не блокирует появление новых causes — они стакаются.
    /// </summary>
    public sealed class StressLoadService
    {
        private readonly SaveData _data;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly ModConfig _config;
        private readonly IMonitor _monitor;
        private HarveyCareTrustService? _trustService;

        private StressLoadState State => _data.StressLoad;

        public StressLoadService(
            SaveData data,
            BuffService buffService,
            StateService stateService,
            ModConfig config,
            IMonitor monitor)
        {
            _data = data;
            _buffService = buffService;
            _stateService = stateService;
            _config = config;
            _monitor = monitor;
        }

        public void SetTrustService(HarveyCareTrustService trustService)
            => _trustService = trustService;

        public int GetCurrentStressLoad() =>
            Math.Clamp(State.CurrentStressLoad, 0, Math.Max(1, _config.MaxStressLoad));

        public int GetMaxStressLoad() => Math.Max(1, _config.MaxStressLoad);

        /// <summary>DEV: установить StressLoad напрямую (causes не меняются).</summary>
        public void SetStressLoadForDebug(int value)
        {
            State.CurrentStressLoad = Math.Clamp(value, 0, GetMaxStressLoad());
            ApplySeverityAndEpisode();
            SyncTreatmentFlags();
            State.LastUpdatedTime = Game1.timeOfDay;

            _monitor.Log(
                $"[StressLoad] Debug set load={GetCurrentStressLoad()} ({State.Severity})",
                LogLevel.Debug);
        }

        public int GetRawStressLoad() => State.CurrentStressLoad;

        public StressSeverity GetSeverity() => State.Severity;

        public IReadOnlyDictionary<string, StressCauseState> GetActiveCauses()
            => State.ActiveCauses;

        public string? GetPrimaryCause() => State.LastPrimaryCause;

        public string? GetCandidateEpisode()
        {
            if (State.HasActiveTreatment && !string.IsNullOrEmpty(State.ActiveTreatmentEpisodeId))
                return null;

            if (State.AwaitingHarveyReview)
                return null;

            var ctx = BuildEpisodeContext();
            return TreatmentEpisodeDefinitions.SelectBestEpisode(ctx)?.EpisodeId;
        }

        public string? GetActiveEpisodeId() => State.ActiveEpisodeId;

        public string? GetActiveTreatmentEpisodeId() => State.ActiveTreatmentEpisodeId;

        public bool HasActiveTreatment() => State.HasActiveTreatment;

        public bool IsAwaitingHarveyReview() => State.AwaitingHarveyReview;

        /// <summary>Добавляет или активирует причину стресса.</summary>
        public void AddCause(string causeId, string? sourceBuffId = null, int? weightOverride = null)
        {
            var now = Game1.timeOfDay;
            var buffId = sourceBuffId
                ?? StressCauses.CauseToBuff.GetValueOrDefault(causeId, causeId);

            if (causeId == StressCauses.GotoroFlashback)
                State.GotoroFlashbackActive = true;

            if (State.ActiveCauses.TryGetValue(causeId, out var existing))
            {
                existing.IsActive = true;
                existing.LastUpdatedTime = now;
                if (!string.IsNullOrEmpty(sourceBuffId))
                    existing.SourceBuffId = sourceBuffId;
            }
            else
            {
                var weight = weightOverride ?? StressCauses.GetBaseWeight(causeId);
                State.ActiveCauses[causeId] = new StressCauseState
                {
                    CauseId = causeId,
                    SourceBuffId = buffId,
                    Weight = weight,
                    IsActive = true,
                    IsSevere = StressCauses.IsSevereCause(causeId),
                    AppliedTime = now,
                    LastUpdatedTime = now,
                    CanSelfResolve = StressCauses.CanSelfResolve(causeId),
                    RequiresHarveyIfSevere = StressCauses.RequiresHarveyIfSevere(causeId),
                };
            }

            Recalculate();
        }

        /// <summary>Добавляет cause по buffId (если buff мапится на cause).</summary>
        public bool AddCauseFromBuff(string buffId)
        {
            if (!StressCauses.TryGetCauseForBuff(buffId, out var causeId))
                return false;

            AddCause(causeId, buffId);
            return true;
        }

        /// <summary>Снимает причину (self-resolve или завершение лечения).</summary>
        public void RemoveCause(string causeId)
        {
            if (causeId == StressCauses.GotoroFlashback)
                State.GotoroFlashbackActive = false;

            if (State.ActiveCauses.Remove(causeId))
                Recalculate();
        }

        /// <summary>Естественное снижение нагрузки (конец дня, отдых).</summary>
        public void DecayStress(int amount)
        {
            if (amount <= 0)
                return;

            State.CurrentStressLoad = Math.Max(0, State.CurrentStressLoad - amount);
            State.CurrentStressLoad = Math.Min(State.CurrentStressLoad, GetMaxStressLoad());
            ApplySeverityAndEpisode();
            State.LastUpdatedTime = Game1.timeOfDay;
        }

        /// <summary>Снижение после лечения у Харви.</summary>
        public void ReduceStressByTreatment(int amount, string? treatmentEpisodeId = null)
        {
            if (amount <= 0)
                return;

            if (_trustService != null)
                amount += _trustService.GetTreatmentStressReductionBonus();

            State.CurrentStressLoad = Math.Max(0, State.CurrentStressLoad - amount);
            State.CurrentStressLoad = Math.Min(State.CurrentStressLoad, GetMaxStressLoad());

            if (!string.IsNullOrEmpty(treatmentEpisodeId))
            {
                State.ActiveTreatmentEpisodeId = null;
                if (string.Equals(State.ActiveEpisodeId, treatmentEpisodeId, StringComparison.Ordinal))
                    State.ActiveEpisodeId = null;
            }

            ApplySeverityAndEpisode();
            SyncTreatmentFlags();
            State.LastUpdatedTime = Game1.timeOfDay;

            _monitor.Log(
                $"[StressLoad] Treatment reduced load by {amount}, now {GetCurrentStressLoad()} ({State.Severity})",
                LogLevel.Debug);
        }

        /// <summary>Устанавливает активный episode лечения (одно назначение Харви).</summary>
        public void SetActiveTreatmentEpisode(string episodeId)
        {
            State.ActiveTreatmentEpisodeId = episodeId;
            State.ActiveEpisodeId = episodeId;
            State.HasActiveTreatment = true;
            State.LastUpdatedTime = Game1.timeOfDay;
        }

        public void SetGotoroFlashbackActive(bool active)
        {
            State.GotoroFlashbackActive = active;
            if (active)
                AddCause(StressCauses.GotoroFlashback);
            else
                RemoveCause(StressCauses.GotoroFlashback);
        }

        /// <summary>
        /// Синхронизирует causes из активных buff/debuff в игре и пересчитывает StressLoad.
        /// </summary>
        public void SyncFromGameState()
        {
            var activeCauseIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (buffId, causeId) in StressCauses.BuffToCause)
            {
                if (!_buffService.HasBuff(buffId))
                    continue;

                activeCauseIds.Add(causeId);
                AddOrRefreshCauseFromBuff(causeId, buffId);
            }

            if (State.GotoroFlashbackActive)
                activeCauseIds.Add(StressCauses.GotoroFlashback);

            var stale = State.ActiveCauses.Keys
                .Where(id => !activeCauseIds.Contains(id))
                .ToList();

            foreach (var causeId in stale)
                State.ActiveCauses.Remove(causeId);

            Recalculate();
        }

        /// <summary>Полный пересчёт StressLoad из ActiveCauses + модификаторы.</summary>
        public void Recalculate()
        {
            var previousLoad = State.CurrentStressLoad;
            var active = State.ActiveCauses.Values.Where(c => c.IsActive).ToList();
            var activeCount = active.Count;

            int load = active.Sum(c => c.Weight);

            if (activeCount >= 5)
                load += 20;
            else if (activeCount >= 3)
                load += 10;

            if (active.Any(c => c.CauseId == StressCauses.Thunder) && Game1.isLightning)
                load += 15;

            if (State.GotoroFlashbackActive || active.Any(c => c.CauseId == StressCauses.GotoroFlashback))
            {
                load += 50;
                if (!State.ActiveCauses.ContainsKey(StressCauses.GotoroFlashback))
                    AddOrRefreshCauseFromBuff(StressCauses.GotoroFlashback, StressCauses.CauseToBuff[StressCauses.GotoroFlashback]);
            }

            load = Math.Min(load, GetMaxStressLoad());

            if (load > previousLoad && _trustService != null)
            {
                var delta = load - previousLoad;
                var mult = _trustService.GetEffectiveStressGainMultiplier();
                load = previousLoad + (int)Math.Ceiling(delta * mult);
                load = Math.Min(load, GetMaxStressLoad());
            }

            State.CurrentStressLoad = load;
            State.LastPrimaryCause = ResolvePrimaryCause(active);
            ApplySeverityAndEpisode();
            SyncTreatmentFlags();
            State.LastUpdatedTime = Game1.timeOfDay;
        }

        /// <summary>Passive decay once per in-game hour.</summary>
        public void ApplyHourlyDecay()
        {
            if (_config.StressDecayPerHour <= 0f)
                return;

            var before = State.CurrentStressLoad;
            State.CurrentStressLoad = Math.Max(
                0,
                State.CurrentStressLoad - (int)MathF.Round(_config.StressDecayPerHour));

            if (State.CurrentStressLoad == before)
                return;

            Recalculate();
            _monitor.Log(
                $"[StressLoad] Hourly decay -{_config.StressDecayPerHour}, now {GetCurrentStressLoad()}",
                LogLevel.Trace);
        }

        public string BuildDebugSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== StressLoad ===");
            sb.AppendLine($"Load: {GetCurrentStressLoad()} (raw {State.CurrentStressLoad})");
            sb.AppendLine($"Severity: {State.Severity}");
            sb.AppendLine($"Primary cause: {State.LastPrimaryCause ?? "(none)"}");
            sb.AppendLine($"Candidate episode: {GetCandidateEpisode() ?? "(none)"}");
            sb.AppendLine(TreatmentEpisodeDefinitions.All.Count > 0
                ? BuildEpisodeDebugLines()
                : "");
            sb.AppendLine($"Active episode: {State.ActiveEpisodeId ?? "(none)"}");
            sb.AppendLine($"Active treatment episode: {State.ActiveTreatmentEpisodeId ?? "(none)"}");
            sb.AppendLine($"HasActiveTreatment: {State.HasActiveTreatment}");
            sb.AppendLine($"AwaitingHarveyReview: {State.AwaitingHarveyReview}");
            sb.AppendLine($"GotoroFlashback: {State.GotoroFlashbackActive}");
            sb.AppendLine("Active causes:");

            if (State.ActiveCauses.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var cause in State.ActiveCauses.Values.OrderByDescending(c => c.Weight))
                {
                    sb.AppendLine(
                        $"  {cause.CauseId}: weight={cause.Weight}, buff={cause.SourceBuffId}, " +
                        $"severe={cause.IsSevere}, selfResolve={cause.CanSelfResolve}");
                }
            }

            return sb.ToString();
        }

        private void AddOrRefreshCauseFromBuff(string causeId, string buffId)
        {
            var now = Game1.timeOfDay;
            var weight = StressCauses.GetBaseWeight(causeId);

            if (State.ActiveCauses.TryGetValue(causeId, out var existing))
            {
                existing.IsActive = true;
                existing.SourceBuffId = buffId;
                existing.Weight = weight;
                existing.LastUpdatedTime = now;
                return;
            }

            State.ActiveCauses[causeId] = new StressCauseState
            {
                CauseId = causeId,
                SourceBuffId = buffId,
                Weight = weight,
                IsActive = true,
                IsSevere = StressCauses.IsSevereCause(causeId),
                AppliedTime = now,
                LastUpdatedTime = now,
                CanSelfResolve = StressCauses.CanSelfResolve(causeId),
                RequiresHarveyIfSevere = StressCauses.RequiresHarveyIfSevere(causeId),
            };
        }

        private void ApplySeverityAndEpisode()
        {
            State.Severity = CalculateSeverity(State.CurrentStressLoad);

            if (State.GotoroFlashbackActive || State.ActiveCauses.ContainsKey(StressCauses.GotoroFlashback))
                State.Severity = StressSeverity.Critical;

            var candidate = GetCandidateEpisode();
            if (candidate != null && string.IsNullOrEmpty(State.ActiveTreatmentEpisodeId))
                State.ActiveEpisodeId = candidate;
        }

        private void SyncTreatmentFlags()
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode?.IsActiveEpisode() == true)
            {
                State.HasActiveTreatment = episode.TreatmentStarted;
                State.AwaitingHarveyReview = episode.AwaitingHarveyReview;
                State.ActiveTreatmentEpisodeId = episode.EpisodeId;
                return;
            }

            var activeTreatments = _data.StressState.ActiveTreatments.Values
                .Where(t => t.IsTreatmentActive())
                .ToList();

            State.HasActiveTreatment = activeTreatments.Any(t => t.TreatmentStarted);
            State.AwaitingHarveyReview = activeTreatments.Any(t => t.AwaitingHarveyReview);

            if (episode == null || episode.IsCompleted)
                State.ActiveTreatmentEpisodeId = null;
        }

        private static StressSeverity CalculateSeverity(int load, ModConfig config)
        {
            var clamped = Math.Clamp(load, 0, Math.Max(1, config.MaxStressLoad));
            return clamped switch
            {
                var l when l >= config.CriticalThreshold => StressSeverity.Critical,
                var l when l >= config.HighThreshold => StressSeverity.High,
                var l when l >= config.MildThreshold => StressSeverity.Mild,
                _ => StressSeverity.Calm,
            };
        }

        private StressSeverity CalculateSeverity(int load) => CalculateSeverity(load, _config);

        private EpisodeEvaluationContext BuildEpisodeContext()
        {
            var activeCauseIds = State.ActiveCauses.Values
                .Where(c => c.IsActive)
                .Select(c => c.CauseId)
                .ToList();

            return new EpisodeEvaluationContext
            {
                StressLoad = GetCurrentStressLoad(),
                Severity = State.Severity,
                ActiveCauseIds = activeCauseIds,
                GotoroFlashbackActive = State.GotoroFlashbackActive,
                WarTraumaFlag = State.WarTraumaFlag,
                IsLightning = Game1.isLightning,
                HasActiveTreatment = State.HasActiveTreatment,
                AwaitingHarveyReview = State.AwaitingHarveyReview,
            };
        }

        private string BuildEpisodeDebugLines()
        {
            var ctx = BuildEpisodeContext();
            var sb = new StringBuilder();
            sb.AppendLine("Episode triggers:");
            foreach (var def in TreatmentEpisodeDefinitions.All)
            {
                sb.AppendLine(
                    $"  {def.EpisodeId}: matched={TreatmentEpisodeDefinitions.MatchesTrigger(def, ctx)}, " +
                    $"priority={def.Priority}, quest={def.QuestId}");
            }

            return sb.ToString();
        }

        private static string? ResolvePrimaryCause(IReadOnlyList<StressCauseState> active)
        {
            if (active.Count == 0)
                return null;

            return active
                .OrderByDescending(c => c.Weight)
                .ThenByDescending(c => c.IsSevere)
                .First()
                .CauseId;
        }
    }
}
