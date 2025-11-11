using Confluent.Kafka;
using Retail.Core;
using System.Text.Json;

namespace Retail.CouponEngine
{
    /// <summary>
    /// Worker di background per il Coupon Engine che consuma eventi Kafka
    /// </summary>
    public class CouponEngineWorker : BackgroundService
    {
        private readonly ILogger<CouponEngineWorker> _logger;
        private readonly IConsumer<string, string> _consumer;
        private readonly RedisStateStore _stateStore; // <-- USA IL NUOVO STATE STORE
        private readonly decimal _spendingThreshold;
        private readonly int _timeWindowDays;

        // Inietta il nuovo RedisStateStore tramite il costruttore
        public CouponEngineWorker(
            ILogger<CouponEngineWorker> logger, 
            IConfiguration configuration, 
            RedisStateStore stateStore)
        {
            _logger = logger;
            _stateStore = stateStore;  

            var topic = configuration["Kafka:TopicName"];

            // Carica la configurazione
            var kafkaConfig = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                GroupId = configuration["Kafka:GroupId"], // Usa il nuovo GroupId
                AutoOffsetReset = AutoOffsetReset.Earliest // Consuma dall'inizio se non ci sono offset
            };

            // Crea il consumer Kafka
            _consumer = new ConsumerBuilder<string, string>(kafkaConfig).Build();

            // Sottoscrivi al topic
            _consumer.Subscribe(topic);

            // Carica le impostazioni specifiche del Coupon Engine
            _spendingThreshold = configuration.GetValue<decimal>("CouponEngine:SpendingThreshold");
            _timeWindowDays = configuration.GetValue<int>("CouponEngine:TimeWindowDays");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
           
            _logger.LogInformation("Consumer Coupon Service in ascolto sul topic Kafka 'attivita-carte-fedelta'...");

            // Ciclo di consumo dei messaggi
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("In attesa di nuovi eventi...");

                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);
                    var loyaltyEvent = JsonSerializer.Deserialize<LoyaltyCardEvent>(
                        consumeResult.Message.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (loyaltyEvent == null)
                        continue;

                    // Usa il RedisStateStore per aggiornare e verificare la spesa
                    var (sogliaSuperata, totaleSpesa) =
                        await _stateStore.UpdateAndCheckSpendAsync(loyaltyEvent, _timeWindowDays, _spendingThreshold);

                    // Se la soglia è stata superata, genera il coupon
                    if (sogliaSuperata)
                    {
                        _logger.LogInformation($" Il Cliente {loyaltyEvent.IdCarta} " +
                            $"ha superato la soglia con Redis! Spesa totale: {totaleSpesa:C}.");

                        //TODO ... logica per generare il coupon  

                        _logger.LogInformation($"Coupon generato per il Cliente {loyaltyEvent.IdCarta} " +
                            $"con spesa totale: {totaleSpesa:C}.");

                        _logger.LogInformation($"Eliminazione dei dati del cliente {loyaltyEvent.IdCarta} da Redis.");
                        await _stateStore.DeleteCustomerDataAsync(loyaltyEvent.IdCarta);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Errore durante il consumo del messaggio: {Error}", ex.Error.Reason);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Errore durante la deserializzazione del messaggio: {Message}", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore imprevisto: {Message}", ex.Message);
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
