using System.Text.Json.Serialization;

namespace AcBoardSimulator.Api.Models
{
    public class DeviceStateDto
    {
        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("supply_air_temperature")]
        public double SupplyAirTemperature { get; set; }

        [JsonPropertyName("return_air_temperature")]
        public double ReturnAirTemperature { get; set; }

        [JsonPropertyName("condensation_air_temperature")]
        public double CondensationAirTemperature { get; set; }

        [JsonPropertyName("outdoor_temperature")]
        public double OutdoorTemperature { get; set; }

        [JsonPropertyName("gas_line_pressure")]
        public double GasLinePressure { get; set; }

        [JsonPropertyName("liquid_line_pressure")]
        public double LiquidLinePressure { get; set; }

        [JsonPropertyName("evaporator_coil_pressure_drop")]
        public double EvaporatorCoilPressureDrop { get; set; }

        [JsonPropertyName("condenser_coil_pressure_drop")]
        public double CondenserCoilPressureDrop { get; set; }
    }
}

