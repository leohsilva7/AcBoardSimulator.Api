namespace AcBoardSimulator.Api.Models
{
    public class TemperatureSensors
    {
        public double SetpointC { get; set; } = 21.0;
        public double IndoorReturnAirC { get; set; } = 23.4;
        public double EvaporatorCoilC { get; set; } = 8.2;
        public double OutdoorAmbientC { get; set; } = 32.5;
        public double CondenserCoilC { get; set; } = 44.8;
        public double CompressorDischargeC { get; set; } = 78.6;
        public double IpmModuleC { get; set; } = 54.1;
    }
}