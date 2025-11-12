using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retail.Core
{
    /// <summary>
    /// Evento pubblicato quando un nuovo coupon viene
    /// generato per un cliente (dal CouponEngine).
    /// Consumato da Retail.NotificationService.
    /// </summary>
    public class CouponGeneratedEvent
    {
        /// <summary>
        /// L'ID della carta fedeltà del cliente.
        /// </summary>
        public string IdCarta { get; set; } = string.Empty;

        /// <summary>
        /// Il codice alfanumerico del coupon generato.
        /// </summary>
        public string CodiceCoupon { get; set; } = string.Empty;

        /// <summary>
        /// Il timestamp di quando l'evento è stato generato.
        /// </summary>
        public DateTime Timestamp { get; set; }

        public DateTime DataScadenza { get; set; }

        /// <summary>
        /// (Opzionale) Breve descrizione del coupon, es: "Sconto 10% sul prossimo acquisto"
        /// Utile per l'invio della notifica.
        /// </summary>
        public string Descrizione { get; set; } = string.Empty;
    }
}
