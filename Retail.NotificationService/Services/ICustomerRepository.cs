using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retail.NotificationService.Services
{
    // Interfaccia per il repository
    public interface ICustomerRepository
    {
        Task<(string? Email, string? Telefono)> GetCustomerContactInfoAsync(string idCarta);
    }

    /// <summary>
    /// Implementazione FITTIZIA (mock) per trovare i dati di contatto.
    /// In un sistema reale, questo interrogherebbe un DB clienti.
    /// </summary>
    public class MockCustomerRepository : ICustomerRepository
    {
        public async Task<(string? Email, string? Telefono)> GetCustomerContactInfoAsync(string idCarta)
        {
            // Simula un ritardo di rete
            await Task.Delay(50);

            // Restituisce dati fittizi basati sull'ID carta
            // (ovviamente qui dovresti cercare nel tuo DB anagrafica clienti)
            if (idCarta.StartsWith("123"))
            {
                return ("mario.rossi@email.com", "+393331234567");
            }

            return ("info@xantrix.it", null); // Non abbiamo il telefono
        }
    }
}
