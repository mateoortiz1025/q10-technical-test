using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orders.Api.Contracts;
using RabbitMQ.Client;

namespace Orders.Api.Messaging;

public interface IOrderEventPublisher
{
    void PublishOrderCreated(OrderCreatedEvent evt);
}

public sealed class RabbitMqOrderPublisher : IOrderEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ConnectionFactory _factory;
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqOrderPublisher(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
        _factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };
    }

    public void PublishOrderCreated(OrderCreatedEvent evt)
    {
        lock (_lock)
        {
            EnsureChannel();

            var json = JsonSerializer.Serialize(evt);
            var body = Encoding.UTF8.GetBytes(json);

            var props = _channel!.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.MessageId = evt.EventId.ToString();

            _channel.BasicPublish(
                exchange: _options.ExchangeName,
                routingKey: RoutingKeys.OrderCreated,
                basicProperties: props,
                body: body);
        }
    }

    private void EnsureChannel()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        _channel?.Dispose();
        _connection?.Dispose();

        _connection = _factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}

