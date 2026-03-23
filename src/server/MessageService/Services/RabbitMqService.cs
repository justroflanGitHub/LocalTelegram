using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace MessageService.Services;

public interface IRabbitMqService
{
    Task PublishMessageEventAsync(string eventType, object data);
    void SubscribeToMessageEvents(string queueName, Action<string, object> handler);
}

public class RabbitMqService : IRabbitMqService, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqService> _logger;
    private const string ExchangeName = "localtelegram.messages";

    public RabbitMqService(string connectionString, ILogger<RabbitMqService> logger)
    {
        _logger = logger;

        try
        {
            // Parse connection string
            var parts = connectionString.Split(';');
            var host = "localhost";
            var username = "guest";
            var password = "guest";

            foreach (var part in parts)
            {
                var keyValue = part.Split('=');
                if (keyValue.Length == 2)
                {
                    switch (keyValue[0].ToLowerInvariant())
                    {
                        case "host":
                            host = keyValue[1];
                            break;
                        case "username":
                            username = keyValue[1];
                            break;
                        case "password":
                            password = keyValue[1];
                            break;
                    }
                }
            }

            var factory = new ConnectionFactory
            {
                HostName = host,
                UserName = username,
                Password = password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange
            _channel.ExchangeDeclare(ExchangeName, ExchangeType.Topic, durable: true);

            _logger.LogInformation("Connected to RabbitMQ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw;
        }
    }

    public Task PublishMessageEventAsync(string eventType, object data)
    {
        try
        {
            var routingKey = eventType;
            var json = JsonSerializer.Serialize(data);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            _channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body
            );

            _logger.LogDebug("Published event {EventType}", eventType);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType}", eventType);
            throw;
        }
    }

    public void SubscribeToMessageEvents(string queueName, Action<string, object> handler)
    {
        // Declare queue
        _channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        // Bind to all message events
        _channel.QueueBind(queue: queueName, exchange: ExchangeName, routingKey: "message.*");

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            try
            {
                var routingKey = ea.RoutingKey;
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var data = JsonSerializer.Deserialize<object>(json);

                handler(routingKey, data!);

                _channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message event");
                _channel.BasicNack(ea.DeliveryTag, false, true);
            }
        };

        _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        _logger.LogInformation("Subscribed to message events on queue {QueueName}", queueName);
    }

    public void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }
}
