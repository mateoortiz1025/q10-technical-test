using Orders.Api.Domain;

namespace Orders.Api.Contracts;

public sealed record CreateOrderRequest(string? ClienteNombre, string? Sku, int Cantidad);

public sealed record OrderResponse(
    Guid Id,
    string ClienteNombre,
    string Sku,
    int Cantidad,
    string Status,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static OrderResponse FromEntity(Order order) => new(
        order.Id,
        order.ClienteNombre,
        order.Sku,
        order.Cantidad,
        order.Status.ToString(),
        order.RejectionReason,
        order.CreatedAt,
        order.UpdatedAt);
}

