using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retail.Core
{
    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// Evento pubblicato quando viene rilevata un'attività sospetta.
    /// </summary>
    public class FraudAlertEvent
    {
        public string IdCarta { get; set; } = string.Empty;
        public string IdTransazioneSospetta { get; set; } = string.Empty;
        public DateTime TimestampAllarme { get; set; } = DateTime.UtcNow;
        public string TipoAllarme { get; set; } = "Unknown";
        public string Messaggio { get; set; } = string.Empty;
        public AlertSeverity Severita { get; set; } = AlertSeverity.Warning;
    }
}
