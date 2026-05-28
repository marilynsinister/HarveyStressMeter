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
        private int _debugCauseLoadFloor;

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

        public int GetCauseLoad() => State.LastCauseLoad;

        public int GetStressRecoveryOffset() => State.StressRecoveryOffset;

        /// <summary>DEV: установить StressLoad напрямую (causes не меняются).</summary>
        public void SetStressLoadForDebug(int value)
        {
            Recalculate();
            var target = Math.Clamp(value, 0, GetMaxStressLoad());
            var causeLoad = State.LastCauseLoad;

            if (target > causeLoad)
            {
                _debugCauseLoadFloor = target;
                State.StressRecoveryOffset = 0;
            }
            else
            {
                _debugCauseLoadFloor = 0;
                State.StressRecoveryOffset = Math.Clamp(causeLoad - target, 0, causeLoad);
            }

            ApplyDebugAdjustedLoad();
            State.LastUpdatedTime = Game1.timeOfDay;

            _monitor.Log(
                $"[StressLoad] Debug set load={GetCurrentStressLoad()} " +
                $"(cause={GetCauseLoad()}, floor={_debugCauseLoadFloor}, offset={GetStressRecoveryOffset()}, {State.Severity})",
                LogLevel.Debug);
        }

        /// <summary>DEV/MCP: сбросить временные debug override (floor).</summary>
        public void ClearDebugOverrides()
        {
            _debugCauseLoadFloor = 0;
        }

        /// <summary>DEV/MCP: увеличить StressRecoveryOffset без удаления active causes.</summary>
        public void DebugApplyRecoveryOffset(int amount, string? reason = null)
        {
            if (amount <= 0)
                return;

            ApplyRecovery(amount);

            _monitor.Log(
                $"[StressLoad] Debug recovery +{amount} ({reason ?? "mcp"}), " +
                $"load={GetCurrentStressLoad()} (cause={GetCauseLoad()}, offset={GetStressRecoveryOffset()}, {State.Severity})",
                LogLevel.Debug);
        }

        /// <summary>DEV/MCP: сбросить StressRecoveryOffset и пересчитать CurrentStressLoad.</summary>
        public void DebugClearRecoveryOffset()
        {
            State.StressRecoveryOffset = 0;
            Recalculate();

            _monitor.Log(
                $"[StressLoad] Debug cleared recovery offset, load={GetCurrentStressLoad()} " +
                $"(cause={GetCauseLoad()}, {State.Severity})",
                LogLevel.Debug);
        }

        /// <summary>DEV/MCP: assert-friendly snapshot для MCP stress_load_debug.</summary>
        public string BuildMcpSnapshot()
        {
            var active = State.ActiveCauses.Values.Where(c => c.IsActive).ToList();
            var activeCount = active.Count;
            var baseWeightSum = active.Sum(c => c.Weight);
            var multiCauseBonus = activeCount >= 5 ? 20 : activeCount >= 3 ? 10 : 0;
            var weatherBonus = active.Any(c => c.CauseId == StressCauses.Thunder) && Game1.isLightning ? 15 : 0;
            var gotoroBonus = State.GotoroFlashbackActive
                              || active.Any(c => c.CauseId == StressCauses.GotoroFlashback)
                ? 50
                : 0;

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"CauseLoad: {GetCauseLoad()}");
            sb.AppendLine($"StressRecoveryOffset: {GetStressRecoveryOffset()}");
            sb.AppendLine($"CurrentStressLoad: {GetCurrentStressLoad()}");
            sb.AppendLine($"LastCauseLoad: {State.LastCauseLoad}");
            sb.AppendLine($"Severity: {State.Severity}");
            sb.AppendLine($"PrimaryCause: {State.LastPrimaryCause ?? "(none)"}");
            sb.AppendLine($"GotoroFlashbackActive: {State.GotoroFlashbackActive}");
            sb.AppendLine($"CandidateEpisode: {GetCandidateEpisode() ?? "(none)"}");
            sb.AppendLine($"thresholdMild: {_config.MildThreshold}");
            sb.AppendLine($"thresholdHigh: {_config.HighThreshold}");
            sb.AppendLine($"thresholdCritical: {_config.CriticalThreshold}");
            sb.AppendLine($"maxStressLoad: {GetMaxStressLoad()}");
            sb.AppendLine($"baseCauseWeightSum: {baseWeightSum}");
            sb.AppendLine($"multiCauseBonus: {multiCauseBonus}");
            sb.AppendLine($"weatherBonus: {weatherBonus}");
            sb.AppendLine($"gotoroBonus: {gotoroBonus}");
            sb.AppendLine($"isLightning: {Game1.isLightning}");
            sb.AppendLine($"activeCauseCount: {activeCount}");
            sb.AppendLine("ActiveCauses:");

            if (active.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var cause in active.OrderByDescending(c => c.Weight))
                {
                    sb.AppendLine(
                        $"  cause: {cause.CauseId}, weight: {cause.Weight}, buff: {cause.SourceBuffId}, " +
                        $"severe: {cause.IsSevere}, selfResolve: {cause.CanSelfResolve}");
                }
            }

            return sb.ToString().TrimEnd();
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

        public bool IsGotoroFlashbackActive() => State.GotoroFlashbackActive;

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

        /// <summary>Естественное снижение нагрузки (конец дня, отдых, аура).</summary>
        public void DecayStress(int amount) => ApplyRecovery(amount);

        /// <summary>Снижение после лечения у Харви.</summary>
        public void ReduceStressByTreatment(int amount, string? treatmentEpisodeId = null)
        {
            if (amount <= 0)
                return;

            if (_trustService != null)
                amount += _trustService.GetTreatmentStressReductionBonus();

            ApplyRecovery(amount);

            if (!string.IsNullOrEmpty(treatmentEpisodeId))
            {
                State.ActiveTreatmentEpisodeId = null;
                if (string.Equals(State.ActiveEpisodeId, treatmentEpisodeId, StringComparison.Ordinal))
                    State.ActiveEpisodeId = null;
            }

            SyncTreatmentFlags();

            _monitor.Log(
                $"[StressLoad] Treatment recovery +{amount} offset, now {GetCurrentStressLoad()} " +
                $"(cause={GetCauseLoad()}, offset={GetStressRecoveryOffset()}, {State.Severity})",
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
            var active = State.ActiveCauses.Values.Where(c => c.IsActive).ToList();
            var activeCount = active.Count;
            var previousCauseLoad = State.LastCauseLoad;
            var previousCurrentLoad = State.CurrentStressLoad;

            var rawCauseLoad = ComputeRawCauseLoadWithGotoro(active, activeCount);
            var adjustedCauseLoad = ApplyTrustToCauseLoad(rawCauseLoad, previousCauseLoad);
            State.LastCauseLoad = adjustedCauseLoad;

            if (activeCount == 0)
            {
                State.StressRecoveryOffset = 0;
            }
            else if (State.StressRecoveryOffset == 0
                     && previousCauseLoad == 0
                     && adjustedCauseLoad > previousCurrentLoad
                     && previousCurrentLoad > 0)
            {
                State.StressRecoveryOffset = Math.Min(adjustedCauseLoad - previousCurrentLoad, adjustedCauseLoad);
            }
            else
            {
                State.StressRecoveryOffset = Math.Min(State.StressRecoveryOffset, adjustedCauseLoad);
            }

            State.CurrentStressLoad = Math.Clamp(
                adjustedCauseLoad - State.StressRecoveryOffset,
                0,
                GetMaxStressLoad());
            State.LastPrimaryCause = ResolvePrimaryCause(active);
            ApplyDebugAdjustedLoad();
            State.LastUpdatedTime = Game1.timeOfDay;
        }

        private void ApplyDebugAdjustedLoad()
        {
            if (_debugCauseLoadFloor <= 0)
            {
                ApplySeverityAndEpisode();
                SyncTreatmentFlags();
                return;
            }

            var effectiveCauseLoad = Math.Max(State.LastCauseLoad, _debugCauseLoadFloor);
            State.StressRecoveryOffset = Math.Min(State.StressRecoveryOffset, effectiveCauseLoad);
            State.CurrentStressLoad = Math.Clamp(
                effectiveCauseLoad - State.StressRecoveryOffset,
                0,
                GetMaxStressLoad());
            ApplySeverityAndEpisode();
            SyncTreatmentFlags();
        }

        /// <summary>Частично «откатывает» recovery offset в конце дня, если causes остаются активными.</summary>
        public void NormalizeRecoveryOffsetAtDayEnd()
        {
            if (State.StressRecoveryOffset <= 0)
                return;

            var hasActiveCauses = State.ActiveCauses.Values.Any(c => c.IsActive);
            if (!hasActiveCauses)
            {
                State.StressRecoveryOffset = 0;
                Recalculate();
                return;
            }

            var normalization = Math.Max(1, State.StressRecoveryOffset / 4);
            State.StressRecoveryOffset = Math.Max(0, State.StressRecoveryOffset - normalization);
            State.StressRecoveryOffset = Math.Min(State.StressRecoveryOffset, State.LastCauseLoad);
            State.CurrentStressLoad = Math.Clamp(
                State.LastCauseLoad - State.StressRecoveryOffset,
                0,
                GetMaxStressLoad());
            ApplySeverityAndEpisode();
            SyncTreatmentFlags();
            State.LastUpdatedTime = Game1.timeOfDay;

            _monitor.Log(
                $"[StressLoad] Day-end offset normalize -{normalization}, now load={GetCurrentStressLoad()} " +
                $"(cause={GetCauseLoad()}, offset={GetStressRecoveryOffset()})",
                LogLevel.Trace);
        }

        /// <summary>Passive decay once per in-game hour.</summary>
        public void ApplyHourlyDecay()
        {
            if (_config.StressDecayPerHour <= 0f)
                return;

            var before = GetCurrentStressLoad();
            ApplyRecovery((int)MathF.Round(_config.StressDecayPerHour));

            if (GetCurrentStressLoad() == before)
                return;

            _monitor.Log(
                $"[StressLoad] Hourly decay +{(int)MathF.Round(_config.StressDecayPerHour)} offset, " +
                $"now {GetCurrentStressLoad()} (cause={GetCauseLoad()}, offset={GetStressRecoveryOffset()})",
                LogLevel.Trace);
        }

        public string BuildDebugSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== StressLoad ===");
            sb.AppendLine($"CauseLoad: {GetCauseLoad()}");
            sb.AppendLine($"StressRecoveryOffset: {GetStressRecoveryOffset()}");
            sb.AppendLine($"CurrentStressLoad: {GetCurrentStressLoad()} (stored {State.CurrentStressLoad})");
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

        private void ApplyRecovery(int amount)
        {
            if (amount <= 0)
                return;

            if (State.LastCauseLoad <= 0)
                Recalculate();

            var causeLoad = State.LastCauseLoad;
            if (causeLoad <= 0)
                return;

            State.StressRecoveryOffset = Math.Min(State.StressRecoveryOffset + amount, causeLoad);
            State.CurrentStressLoad = Math.Clamp(
                causeLoad - State.StressRecoveryOffset,
                0,
                GetMaxStressLoad());
            ApplySeverityAndEpisode();
            SyncTreatmentFlags();
            State.LastUpdatedTime = Game1.timeOfDay;
        }

        private static int ComputeRawCauseLoad(IReadOnlyList<StressCauseState> active, int activeCount)
        {
            int load = active.Sum(c => c.Weight);

            if (activeCount >= 5)
                load += 20;
            else if (activeCount >= 3)
                load += 10;

            if (active.Any(c => c.CauseId == StressCauses.Thunder) && Game1.isLightning)
                load += 15;

            return load;
        }

        private int ComputeRawCauseLoadWithGotoro(IReadOnlyList<StressCauseState> active, int activeCount)
        {
            var load = ComputeRawCauseLoad(active, activeCount);

            if (State.GotoroFlashbackActive || active.Any(c => c.CauseId == StressCauses.GotoroFlashback))
            {
                load += 50;
                if (!State.ActiveCauses.ContainsKey(StressCauses.GotoroFlashback))
                {
                    AddOrRefreshCauseFromBuff(
                        StressCauses.GotoroFlashback,
                        StressCauses.CauseToBuff[StressCauses.GotoroFlashback]);
                }
            }

            return Math.Min(load, GetMaxStressLoad());
        }

        private int ApplyTrustToCauseLoad(int rawCauseLoad, int previousCauseLoad)
        {
            if (rawCauseLoad <= previousCauseLoad || _trustService == null)
                return Math.Min(rawCauseLoad, GetMaxStressLoad());

            var delta = rawCauseLoad - previousCauseLoad;
            var mult = _trustService.GetEffectiveStressGainMultiplier();
            return Math.Min(
                previousCauseLoad + (int)Math.Ceiling(delta * mult),
                GetMaxStressLoad());
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
