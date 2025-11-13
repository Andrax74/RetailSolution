using Confluent.Kafka;
using Retail.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Retail.FraudDetector
{
    /// <summary>
    /// Worker di background per il rilevamento delle frodi.
    /// </summary>
    public class FraudDetectorWorker : BackgroundService
    {
        private readonly ILogger<FraudDetectorWorker> _logger;
        private readonly IConsumer<string, string> _consumer;
        private readonly FraudDetectionStore _store;
        private readonly int _maxTransactions;
        private readonly int _windowSeconds;
        private readonly IProducer<string, string> _producer;
        private readonly string _alertsTopicName;

        public FraudDetectorWorker(
            ILogger<FraudDetectorWorker> logger,
            IConfiguration configuration,
            FraudDetectionStore store,
            IProducer<string, string> producer) // <-- 1. Inietta il producer)
        {
            _logger = logger;
            _store = store;
            _producer = producer; // <-- 2. Assegna il producer

            var kafkaConfig = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                GroupId = configuration["Kafka:GroupId"],
                AutoOffsetReset = AutoOffsetReset.Earliest
            };
            // Crea il consumer Kafka
            _consumer = new ConsumerBuilder<string, string>(kafkaConfig).Build();

            // Sottoscrivi al topic
            _consumer.Subscribe(configuration["Kafka:TopicName"]);

            // Carica le regole di business
            _maxTransactions = configuration.GetValue<int>("FraudDetector:MaxTransactions");
            _windowSeconds = configuration.GetValue<int>("FraudDetector:TimeWindowSeconds");

            _logger.LogInformation(
                "Regole Antifrode caricate: Max {Max} transazioni in {Window} secondi.",
                _maxTransactions, _windowSeconds);
        }

        /// <summary>
        /// Esegue il ciclo di consumo dei messaggi Kafka e applica la logica antifrode.
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Fraud Detector in ascolto sul topic Kafka...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Consuma il messaggio dal topic Kafka
                    var consumeResult = _consumer.Consume(stoppingToken);

                    // Deserializza il messaggio in un oggetto LoyaltyCardEvent
                    var loyaltyEvent = JsonSerializer.Deserialize<LoyaltyCardEvent>(
                        consumeResult.Message.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });


                    if (loyaltyEvent == null) 
                        continue;

                    // Controlla la logica antifrode
                    bool isSuspicious = await _store.IsTransactionSuspiciousAsync(
                        loyaltyEvent, _maxTransactions, _windowSeconds);

                    if (isSuspicious)
                    {
                        // 4. Crea l'evento di allarme
                        var alertEvent = new FraudAlertEvent
                        {
                            IdCarta = loyaltyEvent.IdCarta,
                            IdTransazioneSospetta = loyaltyEvent.IdTransazione ?? "N/D",
                            TipoAllarme = "HighFrequencyTransaction",
                            // Usiamo il metodo helper per un messaggio più ricco
                            Messaggio = $"Rilevate {
                                await _store.GetCurrentCountAsync(loyaltyEvent.IdCarta)} transazioni in {_windowSeconds} secondi.",
                            Severita = AlertSeverity.Critical,
                            TimestampAllarme = DateTime.UtcNow
                        };

                        // 5. Serializza l'evento
                        var eventJson = JsonSerializer.Serialize(alertEvent);

                        // 6. Produci il messaggio sul topic di allarme
                        await _producer.ProduceAsync(_alertsTopicName,
                            new Message<string, string>
                            {
                                Key = alertEvent.IdCarta, // Usa IdCarta come chiave per il partizionamento
                                Value = eventJson
                            },
                            stoppingToken);

                        _logger.LogWarning(
                            "*** ALLARME FRODE RILEVATO e pubblicato su {Topic} *** Carta: {IdCarta}",
                            _alertsTopicName,
                            loyaltyEvent.IdCarta);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Operazione di consumo cancellata.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore non gestito nel Fraud Detector.");
                }
            }
        }

        public override void Dispose()
        {
            _consumer?.Close();
            _consumer?.Dispose();
            base.Dispose();
        }
    }
}
