using Retail.Core;
using StackExchange.Redis;

namespace Retail.FraudDetector
{
    /// <summary>
    /// Gestisce lo stato a breve termine per il rilevamento delle frodi
    /// usando comandi atomici di Redis.
    /// </summary>
    public class FraudDetectionStore
    {
        private readonly ILogger<FraudDetectionStore> _logger;
        private readonly IDatabase _db;

        public FraudDetectionStore(
            IConnectionMultiplexer redis,
            ILogger<FraudDetectionStore> logger)
        {
            _db = redis.GetDatabase();
            _logger = logger;
        }

        /// Genera la chiave Redis per una carta fedeltà
        private string GetKey(string idCarta) => $"fraud_check:{idCarta}";

        /// <summary>
        /// Controlla se una transazione è sospetta incrementando un contatore
        /// in una finestra temporale a scorrimento.
        /// </summary>
        public async Task<bool> IsTransactionSuspiciousAsync(
            LoyaltyCardEvent loyaltyEvent, int maxTransactions, int windowSeconds)
        {
            if (loyaltyEvent == null || string.IsNullOrEmpty(loyaltyEvent.IdCarta))
                return false;

            // Usiamo una chiave univoca per la carta
            var key = $"fraud_check:{loyaltyEvent.IdCarta}";

            try
            {
                // 1. Incrementa il contatore per questa chiave.
                // Questo comando è atomico.
                var currentCount = await _db.StringIncrementAsync(key);

                // 2. Se è la prima volta che vediamo questa chiave (count=1)
                // impostiamo la sua scadenza (es. 60 secondi).
                // La chiave e il suo contatore verranno eliminati automaticamente da Redis.
                if (currentCount == 1)
                {
                    await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));
                }

                // 3. Controlla se il numero di transazioni supera la soglia
                bool isSuspicious = currentCount > maxTransactions;

                if (isSuspicious)
                {
                    _logger.LogWarning(
                        "Rilevamento frode per IdCarta {IdCarta}: {Count} transazioni in {Window} secondi.",
                        loyaltyEvent.IdCarta, currentCount, windowSeconds);
                }

                return isSuspicious;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'accesso a Redis per il rilevamento frodi.");
                // In caso di errore, meglio non bloccare (fail-open)
                return false;
            }
        }

        /// <summary>
        /// Recupera il conteggio corrente delle transazioni per una carta.
        /// </summary>
        /// <param name="idCarta"></param>
        /// <returns></returns>
        public async Task<long> GetCurrentCountAsync(string idCarta)
        {
            var key = GetKey(idCarta);
            // StringGetAsync restituisce una RedisValue.
            // Se la chiave non esiste, è nulla (IsNullOrEmpty).
            var value = await _db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                return 0;
            }
            
            // Facciamo il cast a long (INCR lavora con interi a 64bit)
            return (long)value;
        }
    }
}
