using Microsoft.EntityFrameworkCore;

namespace CsvUploadSample.Entities
{
    public class CsvAppDbContext : DbContext
    {
        public CsvAppDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<CsvMaster> CsvMasters { get; set; }
        public DbSet<SubMaster> SubMasters { get; set; }
        public DbSet<TempCsvMaster> TempCsvMasters { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}
