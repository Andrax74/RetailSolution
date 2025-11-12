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
        private readonly IProducer<string, string> _producer; // <-- 1. Aggiungi producer
        private readonly RedisStateStore _stateStore; // <-- USA IL NUOVO STATE STORE
        private readonly decimal _spendingThreshold;
        private readonly int _timeWindowDays;
        private readonly string _couponTopicName; // <-- 2. Aggiungi nome topic

        // Inietta il nuovo RedisStateStore tramite il costruttore
        public CouponEngineWorker(
            ILogger<CouponEngineWorker> logger, 
            IConfiguration configuration, 
            RedisStateStore stateStore,
            IProducer<string, string> producer) // <-- 3. Inietta producer
        {
            _logger = logger;
            _stateStore = stateStore;
            _producer = producer; // <-- 4. Assegna producer

            var topic = configuration["Kafka:TopicName"];
            var couponTopicName = configuration["Kafka:TopicNameCouponGenerati"]!; // <-- 5. Leggi nome topic output

            // Carica la configurazione del consumer Kafka
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

                        // 1. Simula la generazione del coupon
                        var couponCode = $"WELCOME20-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";
                        var couponEvent = new CouponGeneratedEvent
                        {
                            IdCarta = loyaltyEvent.IdCarta,
                            CodiceCoupon = couponCode,
                            Descrizione = "Sconto 20% sul prossimo acquisto",
                            DataScadenza = DateTime.UtcNow.AddDays(30),
                            Timestamp = DateTime.UtcNow
                        };

                        // 2. Serializza e pubblica sul nuovo topic
                        var eventJson = JsonSerializer.Serialize(couponEvent);
                        await _producer.ProduceAsync(_couponTopicName,
                            new Message<string, string> { Key = couponEvent.IdCarta, Value = eventJson },
                            stoppingToken);

                        _logger.LogInformation($"Coupon generato e pubblicato su '{_couponTopicName}' per il Cliente {loyaltyEvent.IdCarta}");

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
