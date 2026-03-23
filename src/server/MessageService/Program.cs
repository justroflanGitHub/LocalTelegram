using Serilog;
using MessageService.Data;
using MessageService.Hubs;
using MessageService.Services;
using Microsoft.EntityFrameworkCore;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/message-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Message Service...");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    // Add services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "LocalTelegram Message Service",
            Version = "v1",
            Description = "Messaging and Chat Service"
        });
    });

    // Database configuration
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["ConnectionStrings__Postgres"]
        ?? "Host=localhost;Database=localtelegram;Username=localtelegram;Password=localtelegram123";

    builder.Services.AddDbContext<MessageDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Redis configuration
    var redisConnection = builder.Configuration["Redis:Connection"]
        ?? builder.Configuration["Redis__Connection"]
        ?? "localhost:6379";

    builder.Services.AddSingleton<IRedisService>(sp => 
        new RedisService(redisConnection, sp.GetRequiredService<ILogger<RedisService>>()));

    // RabbitMQ configuration
    builder.Services.AddSingleton<IRabbitMqService>(sp =>
        new RabbitMqService(
            builder.Configuration["RabbitMQ:Connection"] 
                ?? builder.Configuration["RabbitMQ__Connection"] 
                ?? "host=localhost;username=localtelegram;password=rabbitmq123",
            sp.GetRequiredService<ILogger<RabbitMqService>>()
        ));

    // Services
    builder.Services.AddScoped<IMessageService, Services.MessageService>();
    builder.Services.AddScoped<IChatService, ChatService>();

    // JWT configuration
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var secretKey = jwtSettings["Secret"] ?? builder.Configuration["JWT_SECRET"] ?? "default-secret-key-change-in-production";

    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer("Bearer", options =>
        {
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"] ?? "localtelegram",
                ValidAudience = jwtSettings["Audience"] ?? "localtelegram-users",
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(secretKey))
            };

            // For SignalR WebSocket authentication
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    // SignalR
    builder.Services.AddSignalR()
        .AddStackExchangeRedis(redisConnection, options =>
        {
            options.Configuration.ChannelPrefix = "LocalTelegram:";
        });

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });

        options.AddPolicy("SignalRPolicy", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    // Health checks
    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString);

    var app = builder.Build();

    // Auto migrate database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MessageDbContext>();
        try
        {
            db.Database.Migrate();
            Log.Information("Database migration completed");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database migration failed or not needed");
        }
    }

    // Configure middleware
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<MessagingHub>("/hubs/messaging");
    app.MapHealthChecks("/health");

    Log.Information("Message Service configured successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Message Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
