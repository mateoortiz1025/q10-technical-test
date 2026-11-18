namespace Orders.Api.Contracts;

public sealed record OrderCreatedEvent(
    Guid EventId,
    Guid OrderId,
    string Sku,
    int Cantidad,
    DateTime OccurredAt);

public sealed record StockReservedEvent(
    Guid EventId,
    Guid OrderId,
    string Sku,
    int Cantidad,
    DateTime OccurredAt);

public sealed record StockRejectedEvent(
    Guid EventId,
    Guid OrderId,
    string Sku,
    int Cantidad,
    string Reason,
    DateTime OccurredAt);

