using Serilog;
using FileService.Data;
using FileService.Services;
using Microsoft.EntityFrameworkCore;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/file-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting File Service...");

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
            Title = "LocalTelegram File Service",
            Version = "v1",
            Description = "File Upload and Storage Service"
        });
    });

    // Database configuration
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? builder.Configuration["ConnectionStrings__Postgres"]
        ?? "Host=localhost;Database=localtelegram;Username=localtelegram;Password=localtelegram123";

    builder.Services.AddDbContext<FileDbContext>(options =>
        options.UseNpgsql(connectionString));

    // MinIO configuration
    var minioConfig = new MinioConfiguration
    {
        Endpoint = builder.Configuration["MinIO:Endpoint"] ?? builder.Configuration["MinIO__Endpoint"] ?? "localhost:9000",
        AccessKey = builder.Configuration["MinIO:AccessKey"] ?? builder.Configuration["MinIO__AccessKey"] ?? "minioadmin",
        SecretKey = builder.Configuration["MinIO:SecretKey"] ?? builder.Configuration["MinIO__SecretKey"] ?? "minioadmin123",
        UseSSL = builder.Configuration.GetValue<bool>("MinIO:UseSSL", false)
    };

    builder.Services.AddSingleton(minioConfig);
    builder.Services.AddSingleton<IStorageService, MinioStorageService>();

    // File service
    builder.Services.AddScoped<IFileService, Services.FileService>();

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

    // Configure Kestrel for large file uploads
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 2147483648; // 2GB
    });

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 2147483648; // 2GB
    });

    var app = builder.Build();

    // Auto migrate database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<FileDbContext>();
        try
        {
            db.Database.Migrate();
            Log.Information("Database migration completed");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database migration failed or not needed");
        }

        // Initialize MinIO bucket
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
        await storageService.EnsureBucketExistsAsync();
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

    Log.Information("File Service configured successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "File Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
