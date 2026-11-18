using System.Text;
using System.Text.Json;
using Inventory.Worker.Contracts;
using Inventory.Worker.Services;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Inventory.Worker.Messaging;

public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<OrderCreatedConsumer> logger) : BackgroundService
{
    private const string QueueName = "inventory.orders-created";
    private const string DlqExchange = "orderflow.dlx";
    private const string DlqQueue = "inventory.orders-created.dlq";

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
                logger.LogWarning(ex, "RabbitMQ connection failed in Inventory.Worker. Retrying in 5s...");
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
        _channel.QueueBind(QueueName, _options.ExchangeName, RoutingKeys.OrderCreated);

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            if (stoppingToken.IsCancellationRequested) return;
            await HandleMessageAsync(ea, stoppingToken);
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);
        logger.LogInformation("Inventory.Worker listening on queue {QueueName}", QueueName);
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
            var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(payload)
                ?? throw new InvalidOperationException("Invalid OrderCreatedEvent payload.");

            using var scope = scopeFactory.CreateScope();
            var reservationService = scope.ServiceProvider.GetRequiredService<StockReservationService>();
            var result = await reservationService.ReserveAsync(evt, ct);

            if (result.Outcome == ReservationOutcome.Reserved)
            {
                var response = new StockReservedEvent(
                    EventId: Guid.NewGuid(),
                    OrderId: evt.OrderId,
                    Sku: evt.Sku,
                    Cantidad: evt.Cantidad,
                    OccurredAt: DateTime.UtcNow);

                PublishResponse(RoutingKeys.StockReserved, response, response.EventId);
            }
            else if (result.Outcome == ReservationOutcome.Rejected)
            {
                var response = new StockRejectedEvent(
                    EventId: Guid.NewGuid(),
                    OrderId: evt.OrderId,
                    Sku: evt.Sku,
                    Cantidad: evt.Cantidad,
                    Reason: result.Reason ?? "Stock rechazado.",
                    OccurredAt: DateTime.UtcNow);

                PublishResponse(RoutingKeys.StockRejected, response, response.EventId);
            }
            else
            {
                logger.LogInformation("Duplicate OrderCreatedEvent {EventId} ignored.", evt.EventId);
            }

            _channel?.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Malformed OrderCreatedEvent; sending to DLQ.");
            _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transient error processing OrderCreatedEvent; requeuing.");
            _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private void PublishResponse<T>(string routingKey, T payload, Guid eventId)
    {
        var json = JsonSerializer.Serialize(payload);
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel!.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        props.MessageId = eventId.ToString();

        _channel.BasicPublish(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            basicProperties: props,
            body: body);
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
