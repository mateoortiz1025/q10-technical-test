using Inventory.Worker.Contracts;
using Inventory.Worker.Data;
using Inventory.Worker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Services;

public enum ReservationOutcome
{
    Reserved,
    Rejected,
    Duplicate
}

public sealed record StockReservationResult(ReservationOutcome Outcome, string? Reason = null)
{
    public static StockReservationResult Reserved() => new(ReservationOutcome.Reserved);
    public static StockReservationResult Rejected(string reason) => new(ReservationOutcome.Rejected, reason);
    public static StockReservationResult Duplicate() => new(ReservationOutcome.Duplicate);
}

public sealed class StockReservationService(InventoryDbContext db)
{
    public async Task<StockReservationResult> ReserveAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var alreadyProcessed = await db.ProcessedEvents
            .AsNoTracking()
            .AnyAsync(e => e.EventId == evt.EventId, ct);

        if (alreadyProcessed)
        {
            await tx.RollbackAsync(ct);
            return StockReservationResult.Duplicate();
        }

        var now = DateTime.UtcNow;
        var sku = evt.Sku.Trim().ToUpperInvariant();

        var affected = await db.Products
            .Where(p => p.Sku == sku && p.Disponible >= evt.Cantidad)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Disponible, p => p.Disponible - evt.Cantidad)
                .SetProperty(p => p.UpdatedAt, now), ct);

        string? rejectionReason = null;
        if (affected == 0)
        {
            var product = await db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Sku == sku, ct);

            rejectionReason = product is null
                ? $"El SKU '{sku}' no existe en el inventario."
                : $"Stock insuficiente para '{sku}'. Disponible: {product.Disponible}, solicitado: {evt.Cantidad}.";
        }

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = evt.EventId,
            OrderId = evt.OrderId,
            ProcessedAt = now
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return affected > 0
            ? StockReservationResult.Reserved()
            : StockReservationResult.Rejected(rejectionReason!);
    }
}
