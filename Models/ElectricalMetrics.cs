namespace AcBoardSimulator.Api.Models
{
    public class ElectricalMetrics
    {
        public double AcInputVoltageV { get; set; } = 220.0;
        public double DcBusVoltageV { get; set; } = 311.0;
        public double TotalCurrentA { get; set; } = 5.85;
        public double CompressorCurrentA { get; set; } = 5.20;
        public double ActivePowerW { get; set; } = 1265.0;
        public double PowerFactor { get; set; } = 0.98;
    }
}