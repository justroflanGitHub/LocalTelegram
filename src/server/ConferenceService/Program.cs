using ConferenceService.Data;
using ConferenceService.Hubs;
using ConferenceService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Text;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/conference-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=conference_service;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ConferenceDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure Redis
var redisConnection = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnection));

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
});

// Register services
builder.Services.AddScoped<IConferenceService, ConferenceService>();

// Configure SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 100 * 1024; // 100KB for large SDP messages
});

// Configure JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "your-super-secret-key-for-jwt-token-generation-min-32-chars";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "LocalTelegram";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "LocalTelegram";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };

    // Configure JWT for SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/signalling"))
            {
                context.Token = accessToken;
            }
            
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition");
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddRedis(redisConnection);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Map SignalR hub
app.MapHub<SignallingHub>("/hubs/signalling");

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ConferenceDbContext>();
    try
    {
        dbContext.Database.EnsureCreated();
        
        // Seed default ICE servers if none exist
        if (!dbContext.IceServers.Any())
        {
            dbContext.IceServers.AddRange(
                new ConferenceService.Models.IceServer
                {
                    Url = "stun:stun.l.google.com:19302",
                    Type = ConferenceService.Models.IceServerType.Stun,
                    IsActive = true,
                    Priority = 10
                },
                new ConferenceService.Models.IceServer
                {
                    Url = "stun:stun1.l.google.com:19302",
                    Type = ConferenceService.Models.IceServerType.Stun,
                    IsActive = true,
                    Priority = 20
                },
                new ConferenceService.Models.IceServer
                {
                    Url = "stun:stun2.l.google.com:19302",
                    Type = ConferenceService.Models.IceServerType.Stun,
                    IsActive = true,
                    Priority = 30
                }
            );
            dbContext.SaveChanges();
            Log.Information("Seeded default STUN servers");
        }
        
        Log.Information("Database schema created/verified successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to create database schema");
    }
}

Log.Information("Conference Service starting on port {Port}", builder.Configuration["PORT"] ?? "5007");

app.Run();
