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

        public FraudDetectorWorker(
            ILogger<FraudDetectorWorker> logger,
            IConfiguration configuration,
            FraudDetectionStore store)
        {
            _logger = logger;
            _store = store;

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


                    if (loyaltyEvent == null) continue;

                    // Controlla la logica antifrode
                    bool isSuspicious = await _store.IsTransactionSuspiciousAsync(
                        loyaltyEvent, _maxTransactions, _windowSeconds);

                    if (isSuspicious)
                    {
                        // Azione!
                        // Per ora logghiamo in modo critico.
                        // Qui potresti produrre un nuovo evento su un topic "fraud-alerts"
                        // o chiamare un'API per bloccare la carta.
                        _logger.LogCritical(
                            "*** ALLARME FRODE RILEVATO *** Carta: {IdCarta}, Transazione: {IdTransazione}",
                            loyaltyEvent.IdCarta, loyaltyEvent.IdTransazione);
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
