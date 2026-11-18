using Inventory.Worker.Contracts;
using Inventory.Worker.Data;
using Inventory.Worker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderFlow.Tests;

/// <summary>
/// Pruebas de integración en memoria (SQLite) para la reserva atómica de stock e idempotencia.
/// </summary>
public sealed class StockReservationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly InventoryDbContext _db;

    public StockReservationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new InventoryDbContext(options);
        _db.Database.EnsureCreated();
        InventorySeeder.SeedAsync(_db).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifica que cuando hay stock suficiente, se descuenta la cantidad solicitada y retorna Reserved.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_SufficientStock_DecrementsStockAndReturnsReserved()
    {
        var service = new StockReservationService(_db);
        var evt = new OrderCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "LAPTOP-PRO-15", 3, DateTime.UtcNow);

        var result = await service.ReserveAsync(evt);

        Assert.Equal(ReservationOutcome.Reserved, result.Outcome);

        var product = await _db.Products.AsNoTracking().SingleAsync(p => p.Sku == "LAPTOP-PRO-15");
        Assert.Equal(12, product.Disponible);
    }

    /// <summary>
    /// Verifica la idempotencia: procesar dos veces el mismo EventId retorna Duplicate y no descuenta doble.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_DuplicateEventId_IsIdempotentAndDoesNotDoubleDecrement()
    {
        var service = new StockReservationService(_db);
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new OrderCreatedEvent(eventId, orderId, "MOUSE-ERG-01", 5, DateTime.UtcNow);

        var first = await service.ReserveAsync(evt);
        var second = await service.ReserveAsync(evt);

        Assert.Equal(ReservationOutcome.Reserved, first.Outcome);
        Assert.Equal(ReservationOutcome.Duplicate, second.Outcome);

        var product = await _db.Products.AsNoTracking().SingleAsync(p => p.Sku == "MOUSE-ERG-01");
        Assert.Equal(45, product.Disponible);
    }

    /// <summary>
    /// Verifica que si no alcanza el stock, retorna Rejected con el motivo y deja el inventario intacto.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_InsufficientStock_ReturnsRejectedAndKeepsStockIntact()
    {
        var service = new StockReservationService(_db);
        var evt = new OrderCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "MONITOR-4K-27", 10, DateTime.UtcNow);

        var result = await service.ReserveAsync(evt);

        Assert.Equal(ReservationOutcome.Rejected, result.Outcome);
        Assert.Contains("Stock insuficiente", result.Reason);

        var product = await _db.Products.AsNoTracking().SingleAsync(p => p.Sku == "MONITOR-4K-27");
        Assert.Equal(3, product.Disponible);
    }

    /// <summary>
    /// Verifica que un SKU inexistente en el inventario retorna Rejected con un mensaje claro.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_UnknownSku_ReturnsRejected()
    {
        var service = new StockReservationService(_db);
        var evt = new OrderCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "NO-EXISTE-99", 1, DateTime.UtcNow);

        var result = await service.ReserveAsync(evt);

        Assert.Equal(ReservationOutcome.Rejected, result.Outcome);
        Assert.Contains("no existe", result.Reason);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
