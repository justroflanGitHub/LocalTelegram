using Serilog;
using AuthService.Data;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/auth-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting Auth Service...");

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
            Title = "LocalTelegram Auth Service",
            Version = "v1",
            Description = "Authentication and User Management Service"
        });
    });

    // Database configuration
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["ConnectionStrings__Postgres"]
        ?? "Host=localhost;Database=localtelegram;Username=localtelegram;Password=localtelegram123";

    builder.Services.AddDbContext<AuthDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Redis configuration
    var redisConnection = builder.Configuration["Redis:Connection"]
        ?? builder.Configuration["Redis__Connection"]
        ?? "localhost:6379";

    builder.Services.AddSingleton<IRedisService>(sp => 
        new RedisService(redisConnection));

    // JWT configuration
    var jwtSettings = builder.Configuration.GetSection("Jwt");
    var secretKey = jwtSettings["Secret"] ?? builder.Configuration["JWT_SECRET"] ?? "default-secret-key-change-in-production";

    builder.Services.AddScoped<IAuthService, AuthService.Services.AuthService>();
    builder.Services.AddScoped<ITokenService, TokenService>();

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
        });

    builder.Services.AddAuthorization();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
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
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
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
    app.MapHealthChecks("/health");

    Log.Information("Auth Service configured successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Auth Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
