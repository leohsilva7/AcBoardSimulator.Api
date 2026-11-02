using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AcBoardSimulator.Api.Models;

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

        public BoardStateService()
        {
            // Cria uma placa padrão ao iniciar para o Frontend já ter algo para carregar
            CreateOrResetBoard("AC-INV-MARILIA-001");
            CreateOrResetBoard("AC-INV-SALA-002"); // Exemplo de uma segunda placa
        }

        public List<string> GetAllBoardIds() => _boards.Keys.ToList();

        public void CreateOrResetBoard(string deviceId)
        {
            var newInstance = new BoardInstance();
            newInstance.State.Metadata.DeviceId = deviceId;
            newInstance.Config.TargetApiUrl = "https://localhost:7100/api/telemetry"; // Sua URL destino padrão

            _boards.AddOrUpdate(deviceId, newInstance, (key, existing) => newInstance);
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

        private static AcTelemetryPayload ClonePayload(AcTelemetryPayload source) =>
            System.Text.Json.JsonSerializer.Deserialize<AcTelemetryPayload>(System.Text.Json.JsonSerializer.Serialize(source));

        private static SimulatorConfig CloneConfig(SimulatorConfig source) =>
            System.Text.Json.JsonSerializer.Deserialize<SimulatorConfig>(System.Text.Json.JsonSerializer.Serialize(source));
    }
}