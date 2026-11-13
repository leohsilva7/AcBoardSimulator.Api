using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AcBoardSimulator.Api.Data;
using AcBoardSimulator.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AcBoardSimulator.Api.Services
{
    public class BoardStateService
    {
        // Contêiner isolado para cada máquina virtual
        private class BoardInstance
        {
            public AcTelemetryPayload State { get; set; } = new();
            public SimulatorConfig Config { get; set; } = new();
            public readonly object InstanceLock = new();
        }

        private readonly ConcurrentDictionary<string, BoardInstance> _boards = new();
        private readonly Random _random = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCache _cache;
        private const string BoardIdsCacheKey = "BoardIds_Active";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        public BoardStateService(IServiceScopeFactory scopeFactory, IMemoryCache cache)
        {
            _scopeFactory = scopeFactory;
            _cache = cache;
        }

        public async Task<List<string>> GetAllBoardIdsAsync(CancellationToken cancellationToken = default)
        {
            var cachedIds = await _cache.GetOrCreateAsync(BoardIdsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var boardGuids = await dbContext.Boards
                    .AsNoTracking()
                    .Where(b => b.DeletedAt == null)
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);

                return boardGuids.Select(id => id.ToString()).ToList();
            });

            var result = cachedIds ?? new List<string>();

            foreach (var id in result)
            {
                EnsureBoardInstance(id);
            }

            return result;
        }

        public List<string> GetAllBoardIds() => GetAllBoardIdsAsync().GetAwaiter().GetResult();

        private BoardInstance EnsureBoardInstance(string deviceId)
        {
            return _boards.GetOrAdd(deviceId, id =>
            {
                var newInstance = new BoardInstance();
                newInstance.State.Metadata.DeviceId = id;
                newInstance.Config.TargetApiUrl = "https://localhost:7100/api/telemetry"; // Sua URL destino padrão
                return newInstance;
            });
        }

        public bool RemoveBoard(string deviceId) => _boards.TryRemove(deviceId, out _);

        private BoardInstance GetInstance(string deviceId)
        {
            if (!_boards.TryGetValue(deviceId, out var instance))
                throw new KeyNotFoundException($"Placa {deviceId} não encontrada.");
            return instance;
        }

        public AcTelemetryPayload GetRawState(string deviceId)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock) { return ClonePayload(instance.State); }
        }

        public DeviceStateDto GetDeviceState(string deviceId)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock)
            {
                var state = instance.State;
                bool applyJitter = instance.Config.ApplySensorJitter && state.OperationalState.PowerState;

                return new DeviceStateDto
                {
                    DeviceId = state.Metadata.DeviceId,
                    SupplyAirTemperature = CalculateSupplyAirTemperature(state, applyJitter),
                    ReturnAirTemperature = state.Temperatures.IndoorReturnAirC,
                    CondensationAirTemperature = state.Temperatures.CondenserCoilC,
                    OutdoorTemperature = state.Temperatures.OutdoorAmbientC,
                    GasLinePressure = state.RefrigerationCycle.SuctionPressurePsi,
                    LiquidLinePressure = state.RefrigerationCycle.DischargePressurePsi,
                    EvaporatorCoilPressureDrop = CalculateEvaporatorPressureDrop(state, applyJitter),
                    CondenserCoilPressureDrop = CalculateCondenserPressureDrop(state, applyJitter)
                };
            }
        }

        public void UpdateState(string deviceId, AcTelemetryPayload newState)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock)
            {
                instance.State = ClonePayload(newState);
                instance.State.Metadata.DeviceId = deviceId; // Força a manter o ID correto
                instance.State.Metadata.TimestampUtc = DateTime.UtcNow;
            }
        }

        public SimulatorConfig GetConfig(string deviceId)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock) { return CloneConfig(instance.Config); }
        }

        public void UpdateConfig(string deviceId, SimulatorConfig newConfig)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock)
            {
                instance.Config.TargetApiUrl = newConfig.TargetApiUrl;
                instance.Config.IntervalMilliseconds = Math.Max(500, newConfig.IntervalMilliseconds);
                instance.Config.IsTransmissionActive = newConfig.IsTransmissionActive;
                instance.Config.ApplySensorJitter = newConfig.ApplySensorJitter;
            }
        }

        public void RecordDispatchResult(string deviceId, int? statusCode, string message)
        {
            if (!_boards.TryGetValue(deviceId, out var instance)) return;
            lock (instance.InstanceLock)
            {
                instance.Config.LastDispatchTimeUtc = DateTime.UtcNow;
                instance.Config.LastHttpStatusCode = statusCode;
                instance.Config.LastDispatchStatusMessage = message;
                if (statusCode.HasValue && statusCode.Value >= 200 && statusCode.Value < 300)
                    instance.Config.TotalPacketsSent++;
            }
        }

        public AcTelemetryPayload GenerateNextTelemetryPacket(string deviceId, int elapsedSeconds)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock)
            {
                if (instance.State.OperationalState.PowerState)
                {
                    instance.State.OperationalState.UptimeSeconds += elapsedSeconds;
                    if (instance.State.Actuators.CompressorFrequencyHz > 0)
                        instance.State.OperationalState.CompressorRuntimeHours += Math.Round(elapsedSeconds / 3600.0, 4);
                }

                // 1. Aplica as regras de física
                ApplyPhysicsRules(instance.State);
                instance.State.Metadata.TimestampUtc = DateTime.UtcNow;
                var packet = ClonePayload(instance.State);

                // 2. Aplica ruído se ativo
                if (instance.Config.ApplySensorJitter && packet.OperationalState.PowerState)
                {
                    packet.Temperatures.IndoorReturnAirC = AddJitter(packet.Temperatures.IndoorReturnAirC, 0.15);
                    packet.Temperatures.CompressorDischargeC = AddJitter(packet.Temperatures.CompressorDischargeC, 0.40);
                    packet.RefrigerationCycle.SuctionPressurePsi = AddJitter(packet.RefrigerationCycle.SuctionPressurePsi, 0.8);
                }
                return packet;
            }
        }

        private void ApplyPhysicsRules(AcTelemetryPayload state)
        {
            if (!state.OperationalState.PowerState || state.Actuators.CompressorFrequencyHz == 0)
            {
                double equalizedPressure = 150.0 + (state.Temperatures.OutdoorAmbientC * 1.5);
                state.RefrigerationCycle.SuctionPressurePsi = Lerp(state.RefrigerationCycle.SuctionPressurePsi, equalizedPressure, 0.05);
                state.RefrigerationCycle.DischargePressurePsi = Lerp(state.RefrigerationCycle.DischargePressurePsi, equalizedPressure, 0.05);
                state.Electrical.CompressorCurrentA = 0;
                return;
            }

            double hz = state.Actuators.CompressorFrequencyHz;
            double eev = state.RefrigerationCycle.EevOpeningSteps;

            double targetDischarge = 200.0 + (hz * 2.8) - (eev * 0.15) + (state.Temperatures.OutdoorAmbientC * 2.0);
            state.RefrigerationCycle.DischargePressurePsi = Lerp(state.RefrigerationCycle.DischargePressurePsi, targetDischarge, 0.1);

            double targetSuction = 180.0 - (hz * 1.5) + (eev * 0.35);
            state.RefrigerationCycle.SuctionPressurePsi = Lerp(state.RefrigerationCycle.SuctionPressurePsi, Math.Max(targetSuction, 20.0), 0.1);

            state.Electrical.CompressorCurrentA = Math.Round((hz * 0.06) + (state.RefrigerationCycle.DischargePressurePsi * 0.004), 2);
            state.Electrical.TotalCurrentA = state.Electrical.CompressorCurrentA + 0.6;
            state.Electrical.ActivePowerW = Math.Round(state.Electrical.TotalCurrentA * state.Electrical.AcInputVoltageV * state.Electrical.PowerFactor, 1);
        }

        public AcTelemetryPayload ApplyPreset(string deviceId, string presetName)
        {
            var instance = GetInstance(deviceId);
            lock (instance.InstanceLock)
            {
                if (presetName == "NORMAL")
                {
                    instance.State.Diagnostics.ErrorCode = "NONE";
                    instance.State.RefrigerationCycle.SuctionPressurePsi = 125;
                }
                else if (presetName == "GAS_LEAK")
                {
                    instance.State.Diagnostics.ErrorCode = "EC";
                    instance.State.RefrigerationCycle.SuctionPressurePsi = 48;
                    instance.State.RefrigerationCycle.EevOpeningSteps = 480;
                }
                // Adicione os outros presets aqui seguindo a lógica do código antigo

                return ClonePayload(instance.State);
            }
        }

        private double Lerp(double current, double target, double rate) => Math.Round(current + (target - current) * rate, 2);
        private double AddJitter(double baseValue, double maxVariance) => Math.Round(baseValue + ((_random.NextDouble() * 2.0 - 1.0) * maxVariance), 2);

        private double NextGaussian(double mean, double stdDev)
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + stdDev * randStdNormal;
        }

        private double CalculateSupplyAirTemperature(AcTelemetryPayload state, bool applyJitter)
        {
            if (!state.OperationalState.PowerState || state.Actuators.IndoorFanRpm <= 0)
            {
                return Math.Round(state.Temperatures.IndoorReturnAirC, 2);
            }

            double tRet = state.Temperatures.IndoorReturnAirC;
            double pSuc = state.RefrigerationCycle.SuctionPressurePsi;
            const double tSup0 = 13.0;
            const double tRet0 = 24.0;
            const double pSuc0 = 120.0;

            double tSup = tSup0 + 0.2 * (tRet - tRet0) + 0.18 * (pSuc - pSuc0);
            double maxAllowed = Math.Max(5.0, tRet - 4.0);
            tSup = Math.Clamp(tSup, 5.0, maxAllowed);

            if (applyJitter)
            {
                tSup += NextGaussian(0.0, 0.2);
                tSup = Math.Clamp(tSup, 5.0, maxAllowed);
            }

            return Math.Round(tSup, 2);
        }

        private double CalculateEvaporatorPressureDrop(AcTelemetryPayload state, bool applyJitter)
        {
            int rpm = state.Actuators.IndoorFanRpm;
            if (!state.OperationalState.PowerState || rpm <= 0)
            {
                return 0.0;
            }

            const double deltaPe0 = 120.0;
            double phiE = state.Diagnostics.AirFilterDirty ? 0.25 : 0.0;
            double w = (state.OperationalState.PowerState && state.Actuators.CompressorFrequencyHz > 0) ? 1.0 : 0.0;
            double fanRatio = rpm / 1050.0;

            double deltaPe = deltaPe0 * (1.0 + phiE) * (1.0 + 0.15 * w) * (fanRatio * fanRatio);

            if (applyJitter)
            {
                deltaPe += NextGaussian(0.0, deltaPe * 0.02);
            }

            return Math.Round(Math.Max(0.0, deltaPe), 2);
        }

        private double CalculateCondenserPressureDrop(AcTelemetryPayload state, bool applyJitter)
        {
            int rpm = state.Actuators.OutdoorFanRpm;
            if (!state.OperationalState.PowerState || rpm <= 0)
            {
                return 0.0;
            }

            const double deltaPc0 = 30.0;
            const double phiC = 0.0;
            double fanRatio = rpm / 720.0;

            double deltaPc = deltaPc0 * (1.0 + phiC) * (fanRatio * fanRatio);

            if (applyJitter)
            {
                deltaPc += NextGaussian(0.0, deltaPc * 0.03);
            }

            return Math.Round(Math.Max(0.0, deltaPc), 2);
        }

        private static AcTelemetryPayload ClonePayload(AcTelemetryPayload source) =>
            System.Text.Json.JsonSerializer.Deserialize<AcTelemetryPayload>(System.Text.Json.JsonSerializer.Serialize(source));

        private static SimulatorConfig CloneConfig(SimulatorConfig source) =>
            System.Text.Json.JsonSerializer.Deserialize<SimulatorConfig>(System.Text.Json.JsonSerializer.Serialize(source));
    }
}