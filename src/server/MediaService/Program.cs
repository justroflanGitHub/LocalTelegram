using MediaService.Data;
using MediaService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using Serilog;
using System.Text;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/media-.txt", rollingInterval: RollingInterval.Day)
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
    ?? "Host=localhost;Database=media_service;Username=postgres;Password=postgres";

builder.Services.AddDbContext<MediaDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure MinIO
var minioEndpoint = builder.Configuration["MinIO:Endpoint"] ?? "localhost:9000";
var minioAccessKey = builder.Configuration["MinIO:AccessKey"] ?? "minioadmin";
var minioSecretKey = builder.Configuration["MinIO:SecretKey"] ?? "minioadmin";
var minioUseSSL = builder.Configuration.GetValue<bool>("MinIO:UseSSL", false);

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    return new MinioClient()
        .WithEndpoint(minioEndpoint)
        .WithCredentials(minioAccessKey, minioSecretKey)
        .WithSSL(minioUseSSL)
        .Build();
});

// Configure Redis for caching
var redisConnection = builder.Configuration["Redis:Connection"] ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
});

// Register services
builder.Services.AddScoped<IVideoProcessingService, VideoProcessingService>();

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
});

builder.Services.AddAuthorization();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure Kestrel for large file uploads
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024; // 2GB
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

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
    try
    {
        dbContext.Database.EnsureCreated();
        Log.Information("Database schema created/verified successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to create database schema");
    }
}

Log.Information("Media Service starting on port {Port}", builder.Configuration["PORT"] ?? "5006");

app.Run();
