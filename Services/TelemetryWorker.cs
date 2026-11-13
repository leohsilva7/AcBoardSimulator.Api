using AcBoardSimulator.Api.Models;
using MQTTnet;
using System.Text.Json;

namespace AcBoardSimulator.Api.Services
{
    public class TelemetryWorker : BackgroundService
    {
        private readonly BoardStateService _boardStateService;
        private readonly ILogger<TelemetryWorker> _logger;
        private readonly IMqttClient _mqttClient;
        private readonly Dictionary<string, DateTime> _lastRunTimes = new();

        public TelemetryWorker(BoardStateService boardStateService, ILogger<TelemetryWorker> logger)
        {
            _boardStateService = boardStateService;
            _logger = logger;

            // Inicializa a fábrica de clientes MQTT
            var mqttFactory = new MqttClientFactory();
            _mqttClient = mqttFactory.CreateMqttClient();
        }

        private async Task EnsureMqttConnectedAsync(CancellationToken token)
        {
            if (_mqttClient.IsConnected) return;

            try
            {
                // Usando um Broker público gratuito para facilitar os testes dos alunos
                var mqttOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer("d895a66657a84666a1d0f741b27e2be8.s1.eu.hivemq.cloud", 8883)
                    .WithClientId($"TestEndpointSSL_{Guid.NewGuid().ToString()[..8]}")
                    .WithCredentials("mario", "senai123")
                    .WithTlsOptions(new MqttClientTlsOptionsBuilder().UseTls().Build())
                    .Build();

                await _mqttClient.ConnectAsync(mqttOptions, token);
                _logger.LogInformation("✅ Conectado ao Broker MQTT (HiveMQ) com sucesso!");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ Falha ao conectar no Broker MQTT: {Message}", ex.Message);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Worker MQTT Multiplexador iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await EnsureMqttConnectedAsync(stoppingToken);

                var boardIds = await _boardStateService.GetAllBoardIdsAsync(stoppingToken);
                var now = DateTime.UtcNow;
                var dispatchTasks = new List<Task>();

                foreach (var deviceId in boardIds)
                {
                    var config = _boardStateService.GetConfig(deviceId);

                    // Só envia se o botão "Iniciar Envio" estiver verde na tela Vue
                    if (!config.IsTransmissionActive) continue;

                    if (!_lastRunTimes.TryGetValue(deviceId, out var lastRun)) { lastRun = now; _lastRunTimes[deviceId] = now; }

                    var elapsedMs = (now - lastRun).TotalMilliseconds;

                    if (elapsedMs >= config.IntervalMilliseconds)
                    {
                        _lastRunTimes[deviceId] = now;
                        int elapsedSeconds = Math.Max(1, (int)(elapsedMs / 1000));

                        // Dispara a publicação da mensagem em paralelo
                        dispatchTasks.Add(PublishTelemetryAsync(deviceId, config, elapsedSeconds, stoppingToken));
                    }
                }

                if (dispatchTasks.Any()) await Task.WhenAll(dispatchTasks);

                await Task.Delay(500, stoppingToken); // Respira meio segundo
            }

            // Desconecta graciosamente quando a API for desligada
            if (_mqttClient.IsConnected)
                await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
        }

        private async Task PublishTelemetryAsync(string deviceId, SimulatorConfig config, int elapsedSeconds, CancellationToken token)
        {
            _boardStateService.GenerateNextTelemetryPacket(deviceId, elapsedSeconds);
            var stateDto = _boardStateService.GetDeviceState(deviceId);
            try
            {
                if (!_mqttClient.IsConnected) return;

                // Transforma os dados em JSON (9 campos em snake_case / deviceId)
                string jsonString = JsonSerializer.Serialize(stateDto);

                // Monta o "Pacote/Envelope" do MQTT
                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic($"telemetria/venther/{deviceId}") // O tópico onde os alunos devem se inscrever
                    .WithPayload(jsonString)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce) // QoS 0
                    .Build();

                // Entrega no Broker (Correio)
                await _mqttClient.PublishAsync(applicationMessage, token);

                // Grava status 200 pro Vue.js mostrar que publicou com sucesso
                _boardStateService.RecordDispatchResult(deviceId, 200, "Publicado (MQTT)");
            }
            catch (Exception ex)
            {
                _boardStateService.RecordDispatchResult(deviceId, null, "Falha MQTT");
            }
        }
    }
}