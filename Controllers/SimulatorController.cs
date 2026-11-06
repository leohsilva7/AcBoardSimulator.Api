using System.Collections.Generic;
using AcBoardSimulator.Api.Models;
using AcBoardSimulator.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcBoardSimulator.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SimulatorController : ControllerBase
    {
        private readonly BoardStateService _boardStateService;

        public SimulatorController(BoardStateService boardStateService)
        {
            _boardStateService = boardStateService;
        }

        [HttpGet("devices")]
        public ActionResult<List<string>> GetAllDevices() => Ok(_boardStateService.GetAllBoardIds());

        [HttpPost("devices/{deviceId}")]
        public ActionResult CreateDevice(string deviceId)
        {
            _boardStateService.CreateOrResetBoard(deviceId);
            return Ok(new { message = $"Dispositivo {deviceId} criado." });
        }

        [HttpGet("{deviceId}/state")]
        public ActionResult<DeviceStateDto> GetState(string deviceId)
        {
            try { return Ok(_boardStateService.GetDeviceState(deviceId)); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpPut("{deviceId}/state")]
        public ActionResult<AcTelemetryPayload> UpdateState(string deviceId, [FromBody] AcTelemetryPayload state)
        {
            try { _boardStateService.UpdateState(deviceId, state); return Ok(_boardStateService.GetRawState(deviceId)); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpGet("{deviceId}/config")]
        public ActionResult<SimulatorConfig> GetConfig(string deviceId)
        {
            try { return Ok(_boardStateService.GetConfig(deviceId)); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpPut("{deviceId}/config")]
        public ActionResult<SimulatorConfig> UpdateConfig(string deviceId, [FromBody] SimulatorConfig config)
        {
            try { _boardStateService.UpdateConfig(deviceId, config); return Ok(_boardStateService.GetConfig(deviceId)); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpPost("{deviceId}/preset/{presetName}")]
        public ActionResult<AcTelemetryPayload> ApplyFaultPreset(string deviceId, string presetName)
        {
            try { return Ok(_boardStateService.ApplyPreset(deviceId, presetName)); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        [HttpPost("/api/telemetry")]
        public ActionResult ReceiveTelemetryDummy([FromBody] object payload)
        {
            // Apenas finge que recebeu os dados do ar-condicionado com sucesso
            return Ok();
        }
    }
}