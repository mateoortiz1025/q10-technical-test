namespace Inventory.Worker.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = "orderflow.events";
}

public static class RoutingKeys
{
    public const string OrderCreated = "order.created";
    public const string StockReserved = "stock.reserved";
    public const string StockRejected = "stock.rejected";
}
