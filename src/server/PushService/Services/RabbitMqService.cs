using Microsoft.AspNetCore.SignalR;
using PushService.Hubs;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace PushService.Services;

public interface IRabbitMqService
{
    Task StartAsync();
    Task PublishNotificationAsync(long userId, Notification notification);
    Task StopAsync();
}

public class RabbitMqService : IRabbitMqService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IRedisService _redisService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RabbitMqService> _logger;
    
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly string _hostname;
    private readonly string _username;
    private readonly string _password;
    private readonly string _exchangeName = "localtelegram.notifications";
    private bool _disposed;

    public RabbitMqService(
        IConfiguration configuration,
        IHubContext<NotificationHub> hubContext,
        IRedisService redisService,
        INotificationService notificationService,
        ILogger<RabbitMqService> logger)
    {
        _configuration = configuration;
        _hubContext = hubContext;
        _redisService = redisService;
        _notificationService = notificationService;
        _logger = logger;

        _hostname = configuration["RabbitMQ:Host"] 
            ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST") 
            ?? "localhost";
        _username = configuration["RabbitMQ:Username"] 
            ?? Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") 
            ?? "guest";
        _password = configuration["RabbitMQ:Password"] 
            ?? Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") 
            ?? "guest";
    }

    public async Task StartAsync()
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _hostname,
                UserName = _username,
                Password = _password,
                DispatchConsumersAsync = true
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            // Declare exchange
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, durable: true);

            // Declare and bind queues for different message types
            await SetupQueuesAsync();

            // Start consumers
            await StartConsumersAsync();

            _logger.LogInformation("RabbitMQ connection established to {Hostname}", _hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw;
        }
    }

    private async Task SetupQueuesAsync()
    {
        if (_channel == null) return;

        // Queue for direct user notifications
        await _channel.QueueDeclareAsync(
            queue: "push.notifications",
            durable: true,
            exclusive: false,
            autoDelete: false);

        await _channel.QueueBindAsync("push.notifications", _exchangeName, "notification.*");

        // Queue for message events
        await _channel.QueueDeclareAsync(
            queue: "push.messages",
            durable: true,
            exclusive: false,
            autoDelete: false);

        await _channel.QueueBindAsync("push.messages", _exchangeName, "message.*");

        // Queue for chat events
        await _channel.QueueDeclareAsync(
            queue: "push.chats",
            durable: true,
            exclusive: false,
            autoDelete: false);

        await _channel.QueueBindAsync("push.chats", _exchangeName, "chat.*");

        // Queue for user status events
        await _channel.QueueDeclareAsync(
            queue: "push.status",
            durable: true,
            exclusive: false,
            autoDelete: false);

        await _channel.QueueBindAsync("push.status", _exchangeName, "status.*");
    }

    private async Task StartConsumersAsync()
    {
        if (_channel == null) return;

        // Consumer for notifications
        var notificationConsumer = new AsyncEventingBasicConsumer(_channel);
        notificationConsumer.ReceivedAsync += async (model, ea) =>
        {
            await HandleNotificationAsync(ea);
            await _channel.BasicAckAsync(ea.DeliveryTag, false);
        };
        await _channel.BasicConsumeAsync("push.notifications", false, notificationConsumer);

        // Consumer for messages
        var messageConsumer = new AsyncEventingBasicConsumer(_channel);
        messageConsumer.ReceivedAsync += async (model, ea) =>
        {
            await HandleMessageEventAsync(ea);
            await _channel.BasicAckAsync(ea.DeliveryTag, false);
        };
        await _channel.BasicConsumeAsync("push.messages", false, messageConsumer);

        // Consumer for chat events
        var chatConsumer = new AsyncEventingBasicConsumer(_channel);
        chatConsumer.ReceivedAsync += async (model, ea) =>
        {
            await HandleChatEventAsync(ea);
            await _channel.BasicAckAsync(ea.DeliveryTag, false);
        };
        await _channel.BasicConsumeAsync("push.chats", false, chatConsumer);

        _logger.LogInformation("RabbitMQ consumers started");
    }

    private async Task HandleNotificationAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            var message = JsonSerializer.Deserialize<NotificationMessage>(body);

            if (message == null || message.UserId == 0)
            {
                _logger.LogWarning("Invalid notification message received");
                return;
            }

            var connections = await _redisService.GetUserConnectionsAsync(message.UserId);

            if (connections.Count == 0)
            {
                // User is offline, store notification for later
                var notification = new Notification
                {
                    Id = message.Id,
                    UserId = message.UserId,
                    Type = message.Type,
                    Title = message.Title,
                    Body = message.Body,
                    Data = message.Data,
                    CreatedAt = DateTime.UtcNow
                };
                await _notificationService.StoreNotificationAsync(notification);
                _logger.LogDebug("User {UserId} offline, notification stored", message.UserId);
                return;
            }

            // Send to all user's connections
            foreach (var connectionId in connections)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("Notification", new
                {
                    message.Id,
                    message.Type,
                    message.Title,
                    message.Body,
                    message.Data,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _logger.LogDebug("Notification {NotificationId} delivered to user {UserId}", 
                message.Id, message.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling notification message");
        }
    }

    private async Task HandleMessageEventAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            var routingKey = ea.RoutingKey;

            if (routingKey == "message.created")
            {
                var messageEvent = JsonSerializer.Deserialize<MessageCreatedEvent>(body);
                if (messageEvent == null) return;

                // Get all chat members and notify them
                var connections = await _redisService.GetUserConnectionsAsync(messageEvent.SenderId);
                
                foreach (var connectionId in connections)
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("MessageCreated", messageEvent);
                }
            }
            else if (routingKey == "message.edited")
            {
                var messageEvent = JsonSerializer.Deserialize<MessageEditedEvent>(body);
                if (messageEvent == null) return;

                await _hubContext.Clients.Group($"chat:{messageEvent.ChatId}").SendAsync("MessageEdited", messageEvent);
            }
            else if (routingKey == "message.deleted")
            {
                var messageEvent = JsonSerializer.Deserialize<MessageDeletedEvent>(body);
                if (messageEvent == null) return;

                await _hubContext.Clients.Group($"chat:{messageEvent.ChatId}").SendAsync("MessageDeleted", messageEvent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message event");
        }
    }

    private async Task HandleChatEventAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            var routingKey = ea.RoutingKey;

            if (routingKey == "chat.member_added")
            {
                var chatEvent = JsonSerializer.Deserialize<ChatMemberEvent>(body);
                if (chatEvent == null) return;

                await _hubContext.Clients.User(chatEvent.UserId.ToString()).SendAsync("AddedToChat", new
                {
                    chatEvent.ChatId,
                    chatEvent.ChatName,
                    AddedBy = chatEvent.AddedBy
                });
            }
            else if (routingKey == "chat.member_removed")
            {
                var chatEvent = JsonSerializer.Deserialize<ChatMemberEvent>(body);
                if (chatEvent == null) return;

                await _hubContext.Clients.User(chatEvent.UserId.ToString()).SendAsync("RemovedFromChat", new
                {
                    chatEvent.ChatId
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling chat event");
        }
    }

    public async Task PublishNotificationAsync(long userId, Notification notification)
    {
        if (_channel == null)
        {
            _logger.LogWarning("RabbitMQ channel not initialized");
            return;
        }

        var message = new NotificationMessage
        {
            Id = notification.Id,
            UserId = userId,
            Type = notification.Type,
            Title = notification.Title,
            Body = notification.Body,
            Data = notification.Data
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json"
        };

        await _channel.BasicPublishAsync(
            exchange: _exchangeName,
            routingKey: $"notification.user.{userId}",
            mandatory: false,
            basicProperties: properties,
            body: body);

        _logger.LogDebug("Published notification for user {UserId}", userId);
    }

    public async Task StopAsync()
    {
        if (_channel != null)
        {
            await _channel.CloseAsync();
        }
        if (_connection != null)
        {
            await _connection.CloseAsync();
        }
        _logger.LogInformation("RabbitMQ connection closed");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _disposed = true;
        }
    }
}

// Message models for RabbitMQ events
public class NotificationMessage
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}

public class MessageCreatedEvent
{
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MessageEditedEvent
{
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
}

public class MessageDeletedEvent
{
    public long MessageId { get; set; }
    public long ChatId { get; set; }
}

public class ChatMemberEvent
{
    public long ChatId { get; set; }
    public string ChatName { get; set; } = string.Empty;
    public long UserId { get; set; }
    public long AddedBy { get; set; }
}
