namespace Orders.Api.Domain;

public sealed class Order
{
    public Guid Id { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

