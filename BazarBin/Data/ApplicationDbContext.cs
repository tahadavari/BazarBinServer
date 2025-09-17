using BazarBin.Models;
using Microsoft.EntityFrameworkCore;

namespace BazarBin.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataSet> DataSets => Set<DataSet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<DataSet>(entity =>
        {
            entity.ToTable("DataSets");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.SchemaName)
                .IsRequired()
                .HasMaxLength(63);

            entity.Property(e => e.TableName)
                .IsRequired()
                .HasMaxLength(63);

            entity.Property(e => e.ImportedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.SchemaName, e.TableName })
                .IsUnique();
        });
    }
}
