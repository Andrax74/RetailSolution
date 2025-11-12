using Microsoft.Extensions.Options;
using Retail.NotificationService.Models;
using System;
using System.Text;
using System.Text.Json;


namespace Retail.NotificationService.Services
{
    public interface ISmsService
    {
        Task SendSmsAsync(string toNumber, string message);
    }

    public class SmsService : ISmsService
    {
        private readonly SmsSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SmsService> _logger;

        public SmsService(
            IOptions<SmsSettings> settings, // ERRORE CORRETTO: IOptions<SmsSettings>
            IHttpClientFactory httpClientFactory,
            ILogger<SmsService> logger)
        {
            _settings = settings.Value; // Qui ora funziona
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task SendSmsAsync(string toNumber, string message)
        {
            _logger.LogInformation("Preparazione invio SMS fittizio a {Number}...", toNumber);

            var httpClient = _httpClientFactory.CreateClient("SmsProvider");

            // Questo è un payload fittizio, varia in base al provider
            var payload = new
            {
                from = _settings.FromNumber,
                to = toNumber,
                text = message
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            // Aggiunge l'autenticazione (fittizia)
            // httpClient.DefaultRequestHeaders.Authorization = 
            //    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            try
            {
                // *** SIMULAZIONE: Non inviamo la richiesta reale ***
                // In un caso reale, decommenteresti la riga seguente:
                // var response = await httpClient.PostAsync(_settings.ApiUrl, content);
                // response.EnsureSuccessStatusCode();

                // Simuliamo un invio OK
                await Task.Delay(100); // Simula latenza di rete

                _logger.LogInformation(
                    "LOG FITTIZIO: Chiamata a {ApiUrl} per inviare SMS a {Number} completata.",
                    _settings.ApiUrl, toNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'invio dell'SMS fittizio a {Number}", toNumber);
            }
        }
    }
}
