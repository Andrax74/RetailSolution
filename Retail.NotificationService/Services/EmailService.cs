using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Security;
using Retail.NotificationService.Models;

namespace Retail.NotificationService.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, 
            string body, bool isHtml = true, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Servizio per l'invio di email utilizzando SMTP via MailKit.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly SmtpSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<SmtpSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SendEmailAsync(
            string toEmail, 
            string subject, 
            string body, 
            bool isHtml = true, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("toEmail non può essere vuoto.", nameof(toEmail));

            if (!MailboxAddress.TryParse(toEmail, out var toAddress))
            {
                _logger.LogError("Indirizzo email destinatario non valido: {Email}", toEmail);
                return; // o throw, a seconda del comportamento desiderato
            }

            var message = new MimeMessage();

            // From
            try
            {
                var fromName = string.IsNullOrWhiteSpace(_settings.FromName) ? 
                    _settings.FromAddress : _settings.FromName;
                
                if (!MailboxAddress.TryParse(_settings.FromAddress, out var fromAddress))
                {
                    _logger.LogError("Indirizzo email mittente non valido nelle impostazioni: {From}", _settings.FromAddress);
                    return;
                }

                message.From.Add(new MailboxAddress(fromName, fromAddress.Address));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella creazione dell'indirizzo mittente.");
                return;
            }

            message.To.Add(toAddress);
            message.Subject = subject ?? string.Empty;

            message.Body = isHtml
                ? new TextPart("html") { Text = body ?? string.Empty }
                : new TextPart("plain") { Text = body ?? string.Empty };

            using var client = new MailKit.Net.Smtp.SmtpClient();

            try
            {
                // Opzione di sicurezza: preferisci StartTls quando richiesto dal server (es. Mailtrap sulla 2525)
                var socketOptions = _settings.UseSsl switch
                {
                    true => SecureSocketOptions.SslOnConnect,
                    false when _settings.Port == 587 => SecureSocketOptions.StartTls,
                    _ => SecureSocketOptions.Auto
                };

                // ConnectAsync supporta CancellationToken nelle versioni recenti
                await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, cancellationToken);

                // Se l'autenticazione è richiesta
                if (!string.IsNullOrEmpty(_settings.Username) || !string.IsNullOrEmpty(_settings.Password))
                {
                    await client.AuthenticateAsync(_settings.Username ?? string.Empty, _settings.Password ?? string.Empty, cancellationToken);
                }

                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);

                _logger.LogInformation("Email inviata con successo a {Email}", toEmail);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Invio email annullato per {Email}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'invio dell'email a {Email}", toEmail);
                // Non rilanciare se vuoi evitare blocchi/riconsumer Kafka; valuta retry/poison-queue altrove.
            }
        }
    }
}
