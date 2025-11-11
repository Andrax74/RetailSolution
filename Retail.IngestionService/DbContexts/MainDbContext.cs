using Microsoft.EntityFrameworkCore;

namespace Retail.IngestionService
{
    public class MainDbContext(DbContextOptions<MainDbContext> options) : DbContext(options)
    {
        public virtual DbSet<Card> Cards { get; set; }
       
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Applica tutte le configurazioni che implementano IEntityTypeConfiguration<T>
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(MainDbContext).Assembly);
        }
    }
    
}
