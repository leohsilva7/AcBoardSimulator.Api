using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AcBoardSimulator.Api.Models
{
    public class DeviceFailureDto
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "CRITICAL"; // WARNING, CRITICAL, FAULT

        [JsonPropertyName("probable_cause")]
        public string ProbableCause { get; set; } = string.Empty;

        [JsonPropertyName("trigger_sensors")]
        public Dictionary<string, double> TriggerSensors { get; set; } = new();
    }
}

