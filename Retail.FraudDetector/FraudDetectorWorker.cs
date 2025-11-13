using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail.Core;
using System;
using System.Text.Json;
using System.Threading;
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
        private readonly JsonSerializerOptions _jsonOptions = 
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };


        public FraudDetectorWorker(
            ILogger<FraudDetectorWorker> logger,
            IConfiguration configuration,
            FraudDetectionStore store,
            IConsumer<string, string> consumer,
            IProducer<string, string> producer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));

            _alertsTopicName = configuration["Kafka:TopicNameAlerts"]
                ?? throw new ArgumentException("Kafka:TopicNameAlerts mancante nella configurazione.");

            _maxTransactions = configuration.GetValue<int>("FraudDetector:MaxTransactions");
            _windowSeconds = configuration.GetValue<int>("FraudDetector:TimeWindowSeconds");

            _logger.LogInformation("Regole Antifrode caricate: Max {Max} transazioni in {Window} secondi.",
                _maxTransactions, _windowSeconds);
        }

        // Esegue il ciclo di consumo ed elaborazione dei messaggi.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Fraud Detector in ascolto sul topic Kafka...");

            // Assicuriamoci che se il token viene cancellato, il consumer venga chiuso
            stoppingToken.Register(() =>
            {
                try
                {
                    _logger.LogInformation("Cancellation requested - closing consumer.");
                    _consumer.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Errore durante la chiusura del consumer su cancellation.");
                }
            });

            // Ciclo di consumo dei messaggi
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> consumeResult = null;

                try
                {
                    consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult == null || string.IsNullOrEmpty(consumeResult.Message?.Value))
                        continue;

                    LoyaltyCardEvent loyaltyEvent;

                    try
                    {
                        loyaltyEvent = JsonSerializer.Deserialize<LoyaltyCardEvent>(
                            consumeResult.Message.Value, _jsonOptions);
                    }
                    catch (JsonException jex)
                    {
                        // Messaggio malformato: log e skip. Potresti voler inoltrare il messaggio a un DLQ.
                        _logger.LogError(jex, "Messaggio JSON malformato. Topic {Topic} Partition {Partition} Offset {Offset}. Value: {Value}",
                            consumeResult.Topic, consumeResult.Partition, consumeResult.Offset, Truncate(consumeResult.Message.Value, 1024));
                        // eventualmente commit dell'offset per non riprocessare, dipende dalla policy
                        _consumer.Commit(consumeResult);
                        continue;
                    }

                    if (loyaltyEvent == null)
                    {
                        _consumer.Commit(consumeResult);
                        continue;
                    }

                    bool isSuspicious = await _store.IsTransactionSuspiciousAsync(
                        loyaltyEvent, _maxTransactions, _windowSeconds);

                    if (isSuspicious)
                    {
                        var currentCount = await _store.GetCurrentCountAsync(loyaltyEvent.IdCarta);

                        var alertEvent = new FraudAlertEvent
                        {
                            IdCarta = loyaltyEvent.IdCarta,
                            IdTransazioneSospetta = loyaltyEvent.IdTransazione ?? "N/D",
                            TipoAllarme = "HighFrequencyTransaction",
                            Messaggio = $"Rilevate {currentCount} transazioni in {_windowSeconds} secondi.",
                            Severita = AlertSeverity.Critical,
                            TimestampAllarme = DateTime.UtcNow
                        };

                        var eventJson = JsonSerializer.Serialize(alertEvent, _jsonOptions);

                        // Produce e aspetta la conferma (con token per cancellazione)
                        await _producer.ProduceAsync(_alertsTopicName,
                            new Message<string, string> { Key = alertEvent.IdCarta, Value = eventJson },
                            stoppingToken);

                        _logger.LogWarning(
                            "*** ALLARME FRODE RILEVATO e pubblicato su {Topic} *** Carta: {IdCarta} TopicPartitionOffset: {Tpo}",
                            _alertsTopicName, loyaltyEvent.IdCarta, $"{consumeResult.Topic}/{consumeResult.Partition}/{consumeResult.Offset}");
                    }

                    // Commit dell'offset solo dopo elaborazione riuscita
                    _consumer.Commit(consumeResult);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Operazione di consumo cancellata - uscita dal loop.");
                    break;
                }
                catch (ConsumeException cex)
                {
                    // Errori del consumer (es. problemi di connessione)
                    _logger.LogError(cex, "Errore nel consumo Kafka: {Reason}", cex.Error.Reason);
                    // potresti aspettare un breve backoff prima di riprovare
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore non gestito nel Fraud Detector.");
                    // non rilanciare: continueremo a consumare i successivi messaggi
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping FraudDetectorWorker...");
            try
            {
                // Flush producer per assicurare invio messaggi in coda
                _producer.Flush(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante il flush del producer.");
            }

            try
            {
                _consumer?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante la chiusura del consumer.");
            }

            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            try
            {
                _consumer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante dispose consumer.");
            }

            try
            {
                _producer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante dispose producer.");
            }

            base.Dispose();
        }

        // Tronca una stringa se supera la lunghezza massima specificata.
        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) 
                return string.Empty;

            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...(truncated)";
        }
    }
}
