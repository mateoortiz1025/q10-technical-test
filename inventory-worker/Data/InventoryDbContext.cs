using Inventory.Worker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<ProductStock> Products => Set<ProductStock>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductStock>(entity =>
        {
            entity.ToTable("product_stock");
            entity.HasKey(p => p.Sku);

            entity.Property(p => p.Sku)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(p => p.Nombre)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(p => p.Disponible)
                .IsRequired();

            entity.Property(p => p.UpdatedAt)
                .IsRequired();
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");
            entity.HasKey(e => e.EventId);

            entity.Property(e => e.OrderId)
                .IsRequired();

            entity.Property(e => e.ProcessedAt)
                .IsRequired();

            entity.HasIndex(e => e.OrderId);
        });
    }
}
