using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Api.Contracts;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Hubs;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Orders.Api.Messaging;

public sealed class OrderEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IHubContext<OrdersHub> hubContext,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderEventsConsumer> logger) : BackgroundService
{
    private const string QueueName = "orders.stock-responses";
    private const string DlqExchange = "orderflow.dlx";
    private const string DlqQueue = "orders.stock-responses.dlq";

    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ConnectAndConsume(stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RabbitMQ consumer connection failed. Retrying in 5s...");
                Cleanup();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void ConnectAndConsume(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ExchangeDeclare(DlqExchange, ExchangeType.Direct, durable: true, autoDelete: false);

        _channel.QueueDeclare(DlqQueue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(DlqQueue, DlqExchange, QueueName);

        var args = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = DlqExchange,
            ["x-dead-letter-routing-key"] = QueueName
        };

        _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
        _channel.QueueBind(QueueName, _options.ExchangeName, RoutingKeys.StockReserved);
        _channel.QueueBind(QueueName, _options.ExchangeName, RoutingKeys.StockRejected);

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            if (stoppingToken.IsCancellationRequested) return;
            await HandleMessageAsync(ea, stoppingToken);
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);
        logger.LogInformation("Orders.Api listening on queue {QueueName}", QueueName);
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
            Order? updatedOrder = null;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            if (ea.RoutingKey == RoutingKeys.StockReserved)
            {
                var evt = JsonSerializer.Deserialize<StockReservedEvent>(payload)
                    ?? throw new InvalidOperationException("Invalid StockReservedEvent payload.");

                updatedOrder = await ApplyStatusUpdateAsync(db, evt.OrderId, OrderStatus.Confirmed, null, ct);
            }
            else if (ea.RoutingKey == RoutingKeys.StockRejected)
            {
                var evt = JsonSerializer.Deserialize<StockRejectedEvent>(payload)
                    ?? throw new InvalidOperationException("Invalid StockRejectedEvent payload.");

                updatedOrder = await ApplyStatusUpdateAsync(db, evt.OrderId, OrderStatus.Rejected, evt.Reason, ct);
            }
            else
            {
                logger.LogWarning("Unknown routing key {RoutingKey}; routing to DLQ.", ea.RoutingKey);
                _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            if (updatedOrder is not null)
            {
                await hubContext.Clients.All.SendAsync(
                    "OrderStatusChanged",
                    OrderResponse.FromEntity(updatedOrder),
                    ct);
            }

            _channel?.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Malformed message on {RoutingKey}; sending to DLQ.", ea.RoutingKey);
            _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transient error processing {RoutingKey}; requeuing.", ea.RoutingKey);
            _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static async Task<Order?> ApplyStatusUpdateAsync(
        OrdersDbContext db,
        Guid orderId,
        OrderStatus targetStatus,
        string? reason,
        CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.Status != OrderStatus.Pending)
            return null;

        order.Status = targetStatus;
        order.RejectionReason = reason;
        order.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return order;
    }

    private void Cleanup()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _channel = null;
        _connection = null;
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}
