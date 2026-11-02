namespace AcBoardSimulator.Api.Models
{
    public class OperationalState
    {
        public bool PowerState { get; set; } = true;
        public string OperationMode { get; set; } = "COOL"; // COOL, HEAT, DRY, FAN, AUTO
        public string FanSpeedSetting { get; set; } = "HIGH"; // LOW, MEDIUM, HIGH, TURBO, AUTO
        public bool SwingEnabled { get; set; } = true;
        public long UptimeSeconds { get; set; } = 0;
        public double CompressorRuntimeHours { get; set; } = 1420.5;
    }
}