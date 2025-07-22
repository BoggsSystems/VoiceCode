using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Identity.Web;
using Serilog;
using VoiceCode.DispatcherService.Configuration;
using VoiceCode.DispatcherService.Hubs;
using VoiceCode.DispatcherService.Middleware;
using VoiceCode.DispatcherService.Services;
using VoiceCode.Common.Interfaces;
using Azure.Identity;
using StackExchange.Redis;
using Azure.Messaging.ServiceBus;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "DispatcherService")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VoiceCode Dispatcher Service");

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
            Title = "VoiceCode Dispatcher Service", 
            Version = "v1",
            Description = "Real-time communication hub for VoiceCode"
        });
    });

    // Add authentication
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

    // Add SignalR with Redis backplane
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    })
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", options =>
    {
        options.Configuration.ChannelPrefix = "VoiceCode";
    })
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

    // Add Application Insights
    builder.Services.AddApplicationInsightsTelemetry();

    // Add Redis cache
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379");
    });

    // Add Service Bus
    builder.Services.AddSingleton(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return new ServiceBusClient(configuration.GetConnectionString("ServiceBus"));
    });

    // Configure options
    builder.Services.Configure<ServiceEndpoints>(builder.Configuration.GetSection("ServiceEndpoints"));
    builder.Services.Configure<DispatcherOptions>(builder.Configuration.GetSection("Dispatcher"));

    // Add services
    builder.Services.AddSingleton<ISessionManager, SessionManager>();
    builder.Services.AddSingleton<IQueueDispatcher, QueueDispatcher>();
    builder.Services.AddSingleton<IServiceRouter, ServiceRouter>();
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();
    builder.Services.AddSingleton<IMetricsService, MetricsService>();
    builder.Services.AddHostedService<QueueProcessorService>();
    builder.Services.AddHostedService<SessionCleanupService>();

    // Add HTTP clients for service communication
    builder.Services.AddHttpClient("STTService", (sp, client) =>
    {
        var endpoints = sp.GetRequiredService<IConfiguration>().GetSection("ServiceEndpoints").Get<ServiceEndpoints>();
        client.BaseAddress = new Uri(endpoints?.STTService ?? "http://localhost:5001");
    });

    builder.Services.AddHttpClient("ClaudeService", (sp, client) =>
    {
        var endpoints = sp.GetRequiredService<IConfiguration>().GetSection("ServiceEndpoints").Get<ServiceEndpoints>();
        client.BaseAddress = new Uri(endpoints?.ClaudeService ?? "http://localhost:5002");
    });

    builder.Services.AddHttpClient("RouterService", (sp, client) =>
    {
        var endpoints = sp.GetRequiredService<IConfiguration>().GetSection("ServiceEndpoints").Get<ServiceEndpoints>();
        client.BaseAddress = new Uri(endpoints?.RouterService ?? "http://localhost:5003");
    });

    builder.Services.AddHttpClient("GeneratorService", (sp, client) =>
    {
        var endpoints = sp.GetRequiredService<IConfiguration>().GetSection("ServiceEndpoints").Get<ServiceEndpoints>();
        client.BaseAddress = new Uri(endpoints?.GeneratorService ?? "http://localhost:5004");
    });

    builder.Services.AddHttpClient("TTSService", (sp, client) =>
    {
        var endpoints = sp.GetRequiredService<IConfiguration>().GetSection("ServiceEndpoints").Get<ServiceEndpoints>();
        client.BaseAddress = new Uri(endpoints?.TTSService ?? "http://localhost:5005");
    });

    // Add health checks
    builder.Services.AddHealthChecks()
        .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", name: "redis")
        .AddSignalRHub<VoiceHub>("voice_hub")
        .AddCheck<ServiceHealthCheck>("services");

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
    app.UseMiddleware<WebSocketMiddleware>();

    app.MapControllers();
    app.MapHub<VoiceHub>("/hubs/voice");
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