using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Identity.Web;
using Serilog;
using VoiceCode.STTService.Configuration;
using VoiceCode.STTService.Middleware;
using VoiceCode.STTService.Services;
using VoiceCode.Common.Interfaces;
using Azure.Identity;
using Azure.Storage.Blobs;
using StackExchange.Redis;
using VoiceCode.STTService.HealthChecks;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "STTService")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VoiceCode STT Service");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.ApplicationInsights(
            services.GetRequiredService<TelemetryConfiguration>(), 
            TelemetryConverter.Traces));

    // Add Azure Key Vault configuration
    if (!builder.Environment.IsDevelopment())
    {
        var keyVaultEndpoint = new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/");
        builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());
    }

    // Add services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new() 
        { 
            Title = "VoiceCode STT Service", 
            Version = "v1",
            Description = "Speech-to-Text service for VoiceCode"
        });
    });

    // Add authentication
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

    // Add Application Insights
    builder.Services.AddApplicationInsightsTelemetry();

    // Add Redis cache with fallback to in-memory
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
    var useRedis = false;

    if (!string.IsNullOrEmpty(redisConnectionString) && 
        redisConnectionString != "localhost:6379" && 
        redisConnectionString != "")
    {
        try
        {
            var redis = ConnectionMultiplexer.Connect(redisConnectionString + ",abortConnect=false,connectTimeout=5000");
            builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
            builder.Services.AddSingleton<ICacheService, RedisCacheService>();
            useRedis = true;
            Log.Information("Successfully connected to Redis");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to connect to Redis, falling back to in-memory cache");
        }
    }

    if (!useRedis)
    {
        builder.Services.AddSingleton<ICacheService, InMemoryCacheService>();
        Log.Information("Using in-memory cache");
    }

    // Add Azure Blob Storage
    builder.Services.AddSingleton(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return new BlobServiceClient(configuration.GetConnectionString("Storage"));
    });

    // Add Speech Services configuration
    builder.Services.Configure<AzureSpeechOptions>(builder.Configuration.GetSection("AzureSpeech"));

    // Add services
    builder.Services.AddScoped<ISTTService, SpeechToTextService>();
    builder.Services.AddScoped<IAudioStorageService, AudioStorageService>();

    // Add health checks
    var healthChecks = builder.Services.AddHealthChecks();
    
    if (useRedis)
    {
        healthChecks.AddRedis(redisConnectionString!, name: "redis");
    }
    
    healthChecks.AddCheck<SpeechServiceHealthCheck>("speech_service");

    // Add CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowWebApp", policy =>
        {
            policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Configure the HTTP request pipeline
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowWebApp");

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    app.MapControllers();
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}