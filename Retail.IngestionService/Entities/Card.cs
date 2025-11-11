using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Retail.IngestionService
{
    public class Card
    {
        [Column("CODFIDELITY")]
        public string CodFidelity { get; set; } = string.Empty;

        [Column("BOLLINI")]
        public int? Bollini { get; set; }

        [Column("ULTIMASPESA")]
        public DateTime? UltimaSpesa { get; set; }

        [Column("OBSOLETO")]
        public string Obsoleto { get; set; } = string.Empty;
    }
}
