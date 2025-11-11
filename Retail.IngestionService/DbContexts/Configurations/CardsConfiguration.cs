using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Retail.IngestionService.Configurations
{
    public class CardsConfiguration : IEntityTypeConfiguration<Card>
    {
        public void Configure(EntityTypeBuilder<Card> builder)
        {
            builder.ToTable("CARDS");

            builder.HasKey(a => a.CodFidelity);
        }
    }
}
