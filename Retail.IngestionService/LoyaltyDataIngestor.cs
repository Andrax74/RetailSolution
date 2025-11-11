using Confluent.Kafka;
using Retail.Core;
using Retail.IngestionService;
using System.Text.Json;

public class LoyaltyDataIngestor : BackgroundService
{
    private readonly ILogger<LoyaltyDataIngestor> _logger;
    private readonly IConsumer<string, string> _consumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public LoyaltyDataIngestor(
        ILogger<LoyaltyDataIngestor> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = "loyalty-ingestion-group",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var topic = configuration["Kafka:TopicName"];

        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        _consumer.Subscribe(topic);
    }

    /// <summary>
    /// Metodo principale di esecuzione del servizio di background
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumer Ingestion Service in ascolto sul topic Kafka 'attivita-carte-fedelta'...");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("In attesa di nuovi eventi...");

            try
            {   // Consuma il messaggio dal topic Kafka
                var consumeResult = _consumer.Consume(stoppingToken);
                //  Deserializza il messaggio in un oggetto LoyaltyCardEvent
                var loyaltyEvent = JsonSerializer.Deserialize<LoyaltyCardEvent>(consumeResult.Message.Value);
                // Estrai l'ID della carta e inizializza il monte bollini
                var idCard = loyaltyEvent?.IdCarta ?? "";

                var monteBollini = 0;

                _logger.LogInformation($"Evento ricevuto per carta: {idCard}");

                if (loyaltyEvent == null)
                {
                    _logger.LogWarning("Evento di carta fedeltà deserializzato come null.");
                    continue;
                }

                //Crea uno scope per ottenere un DbContext "scoped"
                using var scope = _serviceProvider.CreateScope();
                var _db = scope.ServiceProvider.GetRequiredService<MainDbContext>();

                _logger.LogInformation("Recupero carta con codice {Codfid}", idCard);

                // Recupera la carta dal database
                var existingCard = await _db.Cards.FindAsync(idCard);

                if (existingCard == null)
                {
                    _logger.LogWarning("Carta con codice {Codfid} non trovata", idCard);
                    continue;
                }

                _logger.LogInformation("Carta trovata: {@card}", existingCard);

                if (loyaltyEvent.TipoEvento == "Acquisto")
                {
                    _logger.LogInformation("Evento di acquisto ricevuto per carta {Codfid}", idCard);

                    monteBollini = existingCard.Bollini + loyaltyEvent.Dettagli?.Punti ?? 0;
                }
                else if (loyaltyEvent.TipoEvento == "Riscatto")
                {
                    _logger.LogInformation("Evento di riscatto ricevuto per carta {Codfid}", idCard);

                    monteBollini = existingCard.Bollini - loyaltyEvent.Dettagli?.Punti ?? 0;
                }
                else
                {
                    _logger.LogWarning("Tipo di evento non riconosciuto: {TipoEvento}", loyaltyEvent.TipoEvento);
                    continue;
                }

                // Assicurati che i bollini non siano negativi
                existingCard.Bollini = (monteBollini < 0) ? 0 : monteBollini; 
                existingCard.UltimaSpesa = loyaltyEvent.Timestamp; // Aggiorna data l'ultima spesa

                _db.Cards.Update(existingCard);
                var result = await _db.SaveChangesAsync();

                if (result > 0)
                {
                    _logger.LogInformation("Carta con codice {Codfid} aggiornata", idCard);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Operazione di consumo cancellata.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore non gestito nel consumer.");
            }
        }
    }

    public override void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
        base.Dispose();
    }
}
