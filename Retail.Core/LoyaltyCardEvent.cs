using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retail.Core
{
    /// <summary>
    /// Rappresenta il messaggio fondamentale che viene scambiato all'interno del sistema.
    /// </summary>
    public class LoyaltyCardEvent
    {
        public string IdCarta { get; set; } = string.Empty;
        public string IdTransazione { get; set; } = string.Empty;
        public string IdPuntoVendita { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string TipoEvento { get; set; } = string.Empty;
        public LoyaltyCardEventDetails? Dettagli { get; set; }
    }

    public class LoyaltyCardEventDetails
    {
        public decimal Importo { get; set; }
        public int Punti { get; set; }
    }
}
