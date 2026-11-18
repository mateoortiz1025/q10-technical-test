namespace Inventory.Worker.Domain;

public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public DateTime ProcessedAt { get; set; }
}
