using Inventory.Worker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Data;

public static class InventorySeeder
{
    public static async Task SeedAsync(InventoryDbContext db, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var initialProducts = new[]
        {
            new ProductStock { Sku = "LAPTOP-PRO-15", Nombre = "Laptop Pro 15 pulgadas", Disponible = 15, UpdatedAt = now },
            new ProductStock { Sku = "MOUSE-ERG-01", Nombre = "Mouse Ergonómico Inalámbrico", Disponible = 50, UpdatedAt = now },
            new ProductStock { Sku = "TECLADO-MEC-02", Nombre = "Teclado Mecánico RGB", Disponible = 25, UpdatedAt = now },
            new ProductStock { Sku = "MONITOR-4K-27", Nombre = "Monitor 4K 27 pulgadas", Disponible = 3, UpdatedAt = now }
        };

        var existingSkus = await db.Products
            .Select(p => p.Sku)
            .ToListAsync(ct);

        var missing = initialProducts
            .Where(p => !existingSkus.Contains(p.Sku, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
            return;

        db.Products.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }
}
