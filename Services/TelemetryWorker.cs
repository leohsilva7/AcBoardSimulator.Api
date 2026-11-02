using AcBoardSimulator.Api.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AcBoardSimulator.Api.Services
{
    public class TelemetryWorker : BackgroundService
    {
        private readonly BoardStateService _boardStateService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TelemetryWorker> _logger;
        private readonly Dictionary<string, DateTime> _lastRunTimes = new();

        public TelemetryWorker(BoardStateService boardStateService, IHttpClientFactory httpClientFactory, ILogger<TelemetryWorker> logger)
        {
            _boardStateService = boardStateService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var boardIds = _boardStateService.GetAllBoardIds();
                var now = DateTime.UtcNow;
                var dispatchTasks = new List<Task>();

                foreach (var deviceId in boardIds)
                {
                    var config = _boardStateService.GetConfig(deviceId);

                    if (!config.IsTransmissionActive || string.IsNullOrWhiteSpace(config.TargetApiUrl)) continue;

                    if (!_lastRunTimes.TryGetValue(deviceId, out var lastRun)) { lastRun = now; _lastRunTimes[deviceId] = now; }

                    var elapsedMs = (now - lastRun).TotalMilliseconds;

                    if (elapsedMs >= config.IntervalMilliseconds)
                    {
                        _lastRunTimes[deviceId] = now;
                        int elapsedSeconds = Math.Max(1, (int)(elapsedMs / 1000));
                        dispatchTasks.Add(DispatchTelemetryAsync(deviceId, config, elapsedSeconds, stoppingToken));
                    }
                }

                if (dispatchTasks.Any()) await Task.WhenAll(dispatchTasks);

                await Task.Delay(500, stoppingToken); // Respira 500ms e varre as placas novamente
            }
        }

        private async Task DispatchTelemetryAsync(string deviceId, SimulatorConfig config, int elapsedSeconds, CancellationToken token)
        {
            var payload = _boardStateService.GenerateNextTelemetryPacket(deviceId, elapsedSeconds);
            try
            {
                using var client = _httpClientFactory.CreateClient("MaintenanceApiClient");
                using var request = new HttpRequestMessage(HttpMethod.Post, config.TargetApiUrl);
                request.Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                using var response = await client.SendAsync(request, token);
                _boardStateService.RecordDispatchResult(deviceId, (int)response.StatusCode, response.IsSuccessStatusCode ? "OK" : $"Erro {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                _boardStateService.RecordDispatchResult(deviceId, null, "Falha de Conexão");
            }
        }
    }
}