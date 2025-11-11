using Retail.Core;
using StackExchange.Redis;

namespace Retail.CouponEngine
{
    /// <summary>
    /// Gestisce lo stato della spesa dei clienti utilizzando Redis.
    /// </summary>
    public class RedisStateStore
    {
        private readonly ILogger<RedisStateStore> _logger;
        private readonly IDatabase _db;

        public RedisStateStore(
            IConnectionMultiplexer redis,
            ILogger<RedisStateStore> logger)
        {
            _db = redis.GetDatabase();
            _logger = logger;
        }

        /// <summary>
        /// Aggiorna la spesa del cliente e verifica se la soglia è stata superata.
        /// <param name="loyaltyEvent">L'evento della carta fedeltà contenente i dettagli della transazione.</param>
        /// <param name="windowDays">Il numero di giorni per il periodo di spesa (es. 30 giorni).</param>
        /// <param name="threshold">La soglia di spesa da verificare.</param>
        /// </summary>
        public async Task<(bool ThresholdCrossed, decimal TotalSpend)> UpdateAndCheckSpendAsync(
            LoyaltyCardEvent loyaltyEvent, int windowDays, decimal threshold)
        {
            _logger.LogInformation("Ottenuto LoyaltyCardEvent: {@loyaltyEvent}", loyaltyEvent);

            // Validazione dell'input
            if (loyaltyEvent == null || string.IsNullOrEmpty(loyaltyEvent.IdCarta) || loyaltyEvent.IdTransazione == null)
            {
                _logger.LogWarning("Loyalty event is invalid or missing required fields.");
                return (false, 0);
            }

            // Processa solo eventi di tipo "Acquisto"
            if (loyaltyEvent.TipoEvento != "Acquisto" || loyaltyEvent.Dettagli == null)
            {
                return (false, 0);
            }

            // Estrae l'importo della transazione corrente
            var currentAmount = loyaltyEvent.Dettagli.Importo;

            // Chiave Redis per il cliente
            var customerKey = $"customer_spend:{loyaltyEvent.IdCarta}";
            // Timestamp per 30 giorni fa
            var thirtyDaysAgoTimestamp = DateTimeOffset.UtcNow.AddDays(-windowDays).ToUnixTimeSeconds();
            // Timestamp corrente
            var nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 1. Rimuove le transazioni più vecchie del periodo considerato
            _logger.LogInformation("Rimuovo transazioni più vecchie di {Days} giorni per il cliente {CustomerId}",
                windowDays, loyaltyEvent.IdCarta);

            /// Rimuove le transazioni più vecchie di 30 giorni
            await _db.SortedSetRemoveRangeByScoreAsync(customerKey, 0, thirtyDaysAgoTimestamp);

            // 2. Calcola la somma delle transazioni precedenti
            var previousTransactions = await _db.SortedSetRangeByScoreAsync(customerKey);
            var previousTotal = previousTransactions.Sum(t =>
            {
                var parts = t.ToString().Split(':');
                return parts.Length == 2 && decimal.TryParse(parts[1], out var val) ? val : 0;
            });

            _logger.LogInformation("Spesa totale precedente per il cliente {CustomerId}: {PreviousTotal:C}",
                loyaltyEvent.IdCarta, previousTotal);

            // 3. Aggiunge la nuova transazione
            var member = $"{loyaltyEvent.IdTransazione}:{currentAmount}";
            // Aggiunge la transazione con timestamp corrente
            await _db.SortedSetAddAsync(customerKey, member, nowTimestamp);

            // 4. Imposta una scadenza per evitare dati inutili
            // Scade dopo 31 giorni
            await _db.KeyExpireAsync(customerKey, TimeSpan.FromDays(windowDays + 1));

            // 5. Calcola la nuova spesa totale
            var newTotal = previousTotal + currentAmount;

            _logger.LogInformation("Nuova spesa totale per il cliente {CustomerId}: {NewTotal:C}",
                loyaltyEvent.IdCarta, newTotal);

            // 6. Verifica se la soglia è appena stata superata
            bool thresholdCrossed = newTotal >= threshold && previousTotal < threshold;

            return (thresholdCrossed, newTotal);
        }

        /// <summary>   
        /// Eliminazione dei dati del cliente da Redis.
        public async Task<bool> DeleteCustomerDataAsync(string idCarta)
        {
            if (string.IsNullOrWhiteSpace(idCarta))
            {
                _logger.LogWarning("IdCarta non valido per l'eliminazione.");
                return false;
            }

            var customerKey = $"customer_spend:{idCarta}";
            _logger.LogInformation("Eliminazione dei dati Redis per il cliente {CustomerId}", idCarta);

            return await _db.KeyDeleteAsync(customerKey);
        }

    }
}
