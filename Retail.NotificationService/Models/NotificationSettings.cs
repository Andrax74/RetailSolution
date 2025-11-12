using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retail.NotificationService.Models
{
    // Classe contenitore per le impostazioni lette da appsettings.json
    public class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public bool UseSsl { get; set; }
    }

    // Per un provider SMS fittizio (es. Twilio)
    public class SmsSettings
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string FromNumber { get; set; } = string.Empty;
    }
}

