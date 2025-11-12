using Confluent.Kafka;
using Retail.Core;
using Retail.NotificationService.Services;
using System.Text.Json;

namespace Retail.NotificationService
{
    /// <summary>
    /// Worker di background che consuma messaggi Kafka per notifiche.
    /// Gestisce eventi di coupon e allarmi di frode, inviando email e SMS.
    /// </summary>
    public class NotificationWorker : BackgroundService
    {
        private readonly ILogger<NotificationWorker> _logger;
        private readonly IConsumer<string, string> _consumer;
        private readonly IEmailService _emailService;
        private readonly ISmsService _smsService;
        private readonly ICustomerRepository _customerRepository;
        private readonly IConfiguration _configuration;

        private readonly string _couponsTopic;
        private readonly string _alertsTopic;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public NotificationWorker(
            ILogger<NotificationWorker> logger,
            IConfiguration configuration,
            IEmailService emailService,
            ISmsService smsService,
            ICustomerRepository customerRepository)
        {
            // Validazioni degli argomenti
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _smsService = smsService ?? throw new ArgumentNullException(nameof(smsService));
            _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));

            // Legge la configurazione Kafka
            var bootstrap = _configuration["Kafka:BootstrapServers"];
            var groupId = _configuration["Kafka:GroupId"];
            _couponsTopic = _configuration["Kafka:TopicNameCoupons"];
            _alertsTopic = _configuration["Kafka:TopicNameAlerts"];

            // Validazioni di base
            if (string.IsNullOrWhiteSpace(bootstrap))
                throw new InvalidOperationException("Kafka:BootstrapServers non configurato.");
            if (string.IsNullOrWhiteSpace(groupId))
                throw new InvalidOperationException("Kafka:GroupId non configurato.");
            if (string.IsNullOrWhiteSpace(_couponsTopic) && string.IsNullOrWhiteSpace(_alertsTopic))
                throw new InvalidOperationException("Nessun topic Kafka configurato (TopicNameCoupons / TopicNameAlerts).");

            var kafkaConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrap,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                // Disabilitiamo l'auto commit per poter confermare manualmente dopo aver elaborato con successo
                EnableAutoCommit = false,
                // Migliora resilienza in caso di broker non raggiungibile
                SocketKeepaliveEnable = true
            };

            // Crea il consumer Kafka
            _consumer = new ConsumerBuilder<string, string>(kafkaConfig)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
                .SetLogHandler((_, logMessage) => _logger.LogDebug("Kafka log: {Message}", logMessage.Message))
                .Build();

            // Sottoscrizione al/ai topic (se presenti)
            var topics = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(_couponsTopic)) 
                topics.Add(_couponsTopic);
            if (!string.IsNullOrWhiteSpace(_alertsTopic)) 
                topics.Add(_alertsTopic);

            // Sottoscrive ai topic
            _consumer.Subscribe(topics);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NotificationWorker avviato. In ascolto sui topic: {Topics}",
                string.Join(", ", new[] { _couponsTopic, _alertsTopic }));

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Consuma un messaggio (bloccante con token di cancellazione)
                        var consumeResult = _consumer.Consume(stoppingToken);

                        // Controllo nullo (teoricamente non dovrebbe accadere con il token di cancellazione)
                        if (consumeResult == null)
                            continue;

                        // Controllo Message nullo
                        if (consumeResult.Message == null)
                        {
                            _logger.LogWarning("ConsumeResult.Message nullo per offset {Offset} sul topic {Topic}.",
                                consumeResult.Offset, consumeResult.Topic);
                            // confermare comunque l'offset per non rimanere bloccati? dipende dalla policy
                            continue;
                        }

                        var topic = consumeResult.Topic;
                        bool processedSuccessfully = false;

                        try
                        {   // Gestione del messaggio in base al topic
                            if (topic == _couponsTopic)
                            {
                                processedSuccessfully = await SafeHandle(() => 
                                    HandleCouponEventAsync(consumeResult.Message.Value), "HandleCouponEvent");
                            }
                            else if (topic == _alertsTopic)
                            {
                                processedSuccessfully = await SafeHandle(() => 
                                    HandleAlertEventAsync(consumeResult.Message.Value), "HandleAlertEvent");
                            }
                            else
                            {
                                _logger.LogWarning("Messaggio ricevuto su topic non gestito: {Topic}", topic);
                            }

                            // commit manuale se processato correttamente
                            if (processedSuccessfully)
                            {
                                try
                                {
                                    _consumer.Commit(consumeResult);
                                    _logger.LogDebug("Offset {Offset} committato per topic {Topic}.", consumeResult.Offset, topic);
                                }
                                catch (Exception exCommit)
                                {
                                    _logger.LogError(exCommit, "Errore durante il commit dell'offset {Offset} per topic {Topic}.", consumeResult.Offset, topic);
                                }
                            }
                            else
                            {
                                // qui puoi implementare retry, dead-lettering o logging speciale
                                _logger.LogWarning("Messaggio non processato correttamente e non committato (topic {Topic}, offset {Offset}).",
                                    topic, consumeResult.Offset);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("Operazione di gestione messaggio cancellata.");
                            break;
                        }
                    }
                    catch (ConsumeException cex)
                    {
                        _logger.LogError(cex, "Errore nel consumo Kafka: {Reason}", cex.Error.Reason);
                        // piccolo delay per evitare tight-loop in caso di errore continuo
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Esecuzione cancellata (stoppingToken).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore fatale in NotificationWorker.");
                throw;
            }
            finally
            {
                try
                {
                    _logger.LogInformation("Chiusura consumer Kafka...");
                    _consumer.Close(); // tenta di commit/chiudere in modo ordinato
                }
                catch (Exception exClose)
                {
                    _logger.LogWarning(exClose, "Errore durante la chiusura del consumer.");
                }
            }
        }

        /// <summary>
        /// Helper che cattura eccezioni interne e restituisce se l'operazione è avvenuta con successo.
        /// </summary>
        private async Task<bool> SafeHandle(Func<Task> handler, string handlerName)
        {
            try
            {
                await handler();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore in {HandlerName}.", handlerName);
                // Qui potresti inviare il messaggio a una Dead Letter Queue o aumentare contatori di metriche
                return false;
            }
        }

        // Gestione evento coupon
        private async Task HandleCouponEventAsync(string messageValue)
        {
            if (string.IsNullOrWhiteSpace(messageValue))
            {
                _logger.LogWarning("Payload coupon vuoto.");
                return;
            }

            CouponGeneratedEvent? couponEvent;

            try
            {
                couponEvent = JsonSerializer.Deserialize<CouponGeneratedEvent>(messageValue, _jsonOptions);
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex, "JSON coupon non valido: {PayloadPreview}", Preview(messageValue));
                return;
            }

            if (couponEvent == null)
            {
                _logger.LogWarning("Messaggio di coupon deserializzato in null.");
                return;
            }

            _logger.LogInformation("Coupon '{Coupon}' ricevuto per carta '{IdCarta}'.", couponEvent.CodiceCoupon, couponEvent.IdCarta);

            var (email, telefono) = await _customerRepository.GetCustomerContactInfoAsync(couponEvent.IdCarta);

            var notified = false;

            if (!string.IsNullOrWhiteSpace(email))
            {
                var subject = "Hai un nuovo coupon!";
                var body = $"<h1>Complimenti!</h1>" +
                           $"<p>Hai ricevuto un nuovo coupon: <b>{couponEvent.Descrizione}</b>.</p>" +
                           $"<p>Usa il codice <strong>{couponEvent.CodiceCoupon}</strong> " +
                           $"entro il {couponEvent.DataScadenza:dd/MM/yyyy}.</p>";

                await _emailService.SendEmailAsync(email, subject, body);
                notified = true;
                _logger.LogDebug("Email inviata a {Email} per coupon {Coupon}.", email, couponEvent.CodiceCoupon);
            }

            if (!string.IsNullOrWhiteSpace(telefono))
            {
                var message = $"Congratulazioni! Hai un nuovo coupon {couponEvent.Descrizione} ({couponEvent.CodiceCoupon}). Scade il {couponEvent.DataScadenza:dd/MM/yyyy}.";
                await _smsService.SendSmsAsync(telefono, message);
                notified = true;
                _logger.LogDebug("SMS inviato a {Telefono} per coupon {Coupon}.", telefono, couponEvent.CodiceCoupon);
            }

            if (!notified)
            {
                _logger.LogWarning("Nessun contatto trovato per IdCarta {IdCarta}. Impossibile notificare.", couponEvent.IdCarta);
            }
        }

        // Gestione evento allarme frode
        private async Task HandleAlertEventAsync(string messageValue)
        {
            if (string.IsNullOrWhiteSpace(messageValue))
            {
                _logger.LogWarning("Payload alert vuoto.");
                return;
            }

            FraudAlertEvent? alertEvent;
            try
            {
                alertEvent = JsonSerializer.Deserialize<FraudAlertEvent>(messageValue, _jsonOptions);
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex, "JSON alert non valido: {PayloadPreview}", Preview(messageValue));
                return;
            }

            if (alertEvent == null)
            {
                _logger.LogWarning("Messaggio di alert deserializzato in null.");
                return;
            }

            _logger.LogWarning("Allarme frode '{Severity}' ricevuto per carta '{IdCarta}': {Message}",
                alertEvent.Severita, alertEvent.IdCarta, alertEvent.Messaggio);

            var subject = $"[ALLARME FRODE {alertEvent.Severita}] Carta: {alertEvent.IdCarta}";
            var body = $"<h1>Allarme di Frode Rilevato</h1>" +
                       $"<p><strong>Carta:</strong> {alertEvent.IdCarta}</p>" +
                       $"<p><strong>Severità:</strong> {alertEvent.Severita}</p>" +
                       $"<p><strong>Tipo:</strong> {alertEvent.TipoAllarme}</p>" +
                       $"<p><strong>Messaggio:</strong> {alertEvent.Messaggio}</p>" +
                       $"<p><strong>Timestamp:</strong> {alertEvent.TimestampAllarme}</p>";

            var alertEmails = _configuration.GetSection("Alerting:EmailAddresses").Get<string[]>();

            if (alertEmails == null || alertEmails.Length == 0)
            {
                _logger.LogError("Nessun indirizzo email configurato in 'Alerting:EmailAddresses' per inviare l'allarme.");
            }
            else
            {
                foreach (var email in alertEmails)
                {
                    if (string.IsNullOrWhiteSpace(email))
                        continue;

                    await _emailService.SendEmailAsync(email, subject, body);
                }

                _logger.LogInformation("Allarme frode inviato via email a {EmailCount} destinatari.", alertEmails.Length);
            }

            // Opzionale: invio SMS a numero di emergenza se configurato
            var emergencyNumber = _configuration["Alerting:EmergencyPhone"];
            if (!string.IsNullOrWhiteSpace(emergencyNumber))
            {
                var shortMsg = $"Allarme frode ({alertEvent.Severita}) su carta {alertEvent.IdCarta}. Controllare email.";
                await _smsService.SendSmsAsync(emergencyNumber, shortMsg);
                _logger.LogInformation("SMS di emergenza inviato a {Phone}.", emergencyNumber);
            }
        }

        // Helper per anteprima stringa
        private static string Preview(string s, int max = 200) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "...");

        // Cleanup risorse
        public override void Dispose()
        {
            try
            {
                _consumer?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Errore durante Close() in Dispose().");
            }
            finally
            {
                _consumer?.Dispose();
                base.Dispose();
            }
        }

        // Log di stop 
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("NotificationWorker stopping...");
            await base.StopAsync(cancellationToken);
        }
    }
}
