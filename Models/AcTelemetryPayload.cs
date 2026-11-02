using System;

namespace AcBoardSimulator.Api.Models
{
    public class AcTelemetryPayload
    {
        public BoardMetadata Metadata { get; set; } = new();
        public OperationalState OperationalState { get; set; } = new();
        public TemperatureSensors Temperatures { get; set; } = new();
        public RefrigerationCycle RefrigerationCycle { get; set; } = new();
        public ElectricalMetrics Electrical { get; set; } = new();
        public ActuatorStates Actuators { get; set; } = new();
        public DiagnosticStatus Diagnostics { get; set; } = new();
    }

    public class BoardMetadata
    {
        public string DeviceId { get; set; } = "AC-INV-001";
        public string SerialNumber { get; set; } = "SN2026098841BR";
        public string BoardModel { get; set; } = "INV-CTRL-V4.2";
        public string FirmwareVersion { get; set; } = "2.4.1";
        public string RefrigerantType { get; set; } = "R410A";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string SimulationMode { get; set; } = "MANUAL_OVERRIDE";
    }

    public class RefrigerationCycle
    {
        public double SuctionPressurePsi { get; set; } = 124.5;
        public double DischargePressurePsi { get; set; } = 368.0;
        public int EevOpeningSteps { get; set; } = 240; // 0 a 480 passos
        public double SuperheatC { get; set; } = 6.4;
        public double SubcoolingC { get; set; } = 5.2;
    }

    public class ActuatorStates
    {
        public double CompressorFrequencyHz { get; set; } = 58.0;
        public int IndoorFanRpm { get; set; } = 1050;
        public int OutdoorFanRpm { get; set; } = 720;
        public bool FourWayValveActive { get; set; } = false;
    }

    public class DiagnosticStatus
    {
        public string ErrorCode { get; set; } = "NONE";
        public string ErrorDescription { get; set; } = "Operação normal";
        public bool ProtectionTripped { get; set; } = false;
        public bool InterBoardCommOk { get; set; } = true;
        public bool AirFilterDirty { get; set; } = false;
    }

    public class SimulatorConfig
    {
        public string TargetApiUrl { get; set; } = "https://localhost:7100/api/telemetry";
        public string ApiKeyHeader { get; set; } = "X-Device-Api-Key";
        public string ApiKeyValue { get; set; } = "secret-board-token-123";
        public int IntervalMilliseconds { get; set; } = 3000;
        public bool IsTransmissionActive { get; set; } = true;
        public bool ApplySensorJitter { get; set; } = true;
        public DateTime? LastDispatchTimeUtc { get; set; }
        public int? LastHttpStatusCode { get; set; }
        public string LastDispatchStatusMessage { get; set; } = "Aguardando primeiro envio...";
        public long TotalPacketsSent { get; set; } = 0;
    }
}