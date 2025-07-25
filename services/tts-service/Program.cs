using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Identity.Web;
using Serilog;
using VoiceCode.TTSService.Configuration;
using VoiceCode.TTSService.Middleware;
using VoiceCode.TTSService.Services;
using VoiceCode.TTSService.Services.Interfaces;
using VoiceCode.Common.Interfaces;
using Azure.Identity;
using Azure.Storage.Blobs;
using StackExchange.Redis;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "TTSService")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VoiceCode TTS Service");

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

    // Explicitly set configuration values from environment variables
    var speechKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
    var speechRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
    var serviceBusConnectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
    
    if (!string.IsNullOrEmpty(speechKey))
    {
        builder.Configuration["AzureSpeech:Key"] = speechKey;
        Log.Information("Azure Speech Key configured from environment variable");
    }
    
    if (!string.IsNullOrEmpty(speechRegion))
    {
        builder.Configuration["AzureSpeech:Region"] = speechRegion;
        Log.Information("Azure Speech Region configured: {Region}", speechRegion);
    }
    
    if (!string.IsNullOrEmpty(serviceBusConnectionString))
    {
        builder.Configuration["ConnectionStrings:ServiceBus"] = serviceBusConnectionString;
    }

    // Add services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new() 
        { 
            Title = "VoiceCode TTS Service", 
            Version = "v1",
            Description = "Text-to-Speech service for VoiceCode"
        });
    });

    // Add HttpClient for Azure TTS REST API
    builder.Services.AddHttpClient("AzureTTS", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
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
    builder.Services.Configure<VoiceOptions>(builder.Configuration.GetSection("Voice"));

    // Add services
    builder.Services.AddScoped<VoiceCode.TTSService.Services.Interfaces.ITTSService, TextToSpeechService>();
    builder.Services.AddScoped<VoiceCode.Common.Interfaces.ITTSService, TextToSpeechService>();
    builder.Services.AddScoped<VoiceCode.TTSService.Services.Interfaces.IAudioStorageService, AudioStorageService>();
    builder.Services.AddScoped<VoiceCode.Common.Interfaces.IAudioStorageService, AudioStorageService>();
    builder.Services.AddSingleton<IVoicePersonalityService, VoicePersonalityService>();
    builder.Services.AddSingleton<ISSMLBuilder, SSMLBuilderService>();
    
    // Add background service for Service Bus processing
    builder.Services.AddHostedService<TTSQueueProcessor>();

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