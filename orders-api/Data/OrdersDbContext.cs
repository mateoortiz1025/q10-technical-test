using Microsoft.EntityFrameworkCore;
using Orders.Api.Domain;

namespace Orders.Api.Data;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(o => o.Id);

            entity.Property(o => o.ClienteNombre)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(o => o.Sku)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(o => o.Cantidad)
                .IsRequired();

            entity.Property(o => o.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(24);

            entity.Property(o => o.RejectionReason)
                .HasMaxLength(256);

            entity.Property(o => o.CreatedAt)
                .IsRequired();

            entity.Property(o => o.UpdatedAt)
                .IsRequired();

            entity.HasIndex(o => o.CreatedAt);
        });
    }
}

