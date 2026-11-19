using AcBoardSimulator.Api.Models;

namespace AcBoardSimulator.Api.Services
{
    public interface IFailureDetectionService
    {
        DeviceFailureDto? EvaluateFailure(DeviceStateDto dto, AcTelemetryPayload payload);
    }
}

