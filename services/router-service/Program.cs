using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Identity.Web;
using Serilog;
using VoiceCode.RouterService.Configuration;
using VoiceCode.RouterService.Middleware;
using VoiceCode.RouterService.Services;
using VoiceCode.Common.Interfaces;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using StackExchange.Redis;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "RouterService")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VoiceCode Router Service");

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
            Title = "VoiceCode Router Service", 
            Version = "v1",
            Description = "Intent classification and request routing service for VoiceCode"
        });
    });

    // Add authentication
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

    // Add Application Insights
    builder.Services.AddApplicationInsightsTelemetry();

    // Add Redis cache
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379");
    });

    // Add Service Bus client
    builder.Services.AddSingleton<ServiceBusClient>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return new ServiceBusClient(configuration.GetConnectionString("ServiceBus"));
    });

    // Add configuration
    builder.Services.Configure<RouterOptions>(builder.Configuration.GetSection("Router"));
    builder.Services.Configure<IntentClassificationOptions>(builder.Configuration.GetSection("IntentClassification"));

    // Add services
    builder.Services.AddScoped<IPromptRouter, PromptRouterService>();
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();
    builder.Services.AddSingleton<IIntentClassifier, IntentClassifierService>();
    builder.Services.AddSingleton<IContextManager, ContextManagerService>();
    builder.Services.AddSingleton<IPromptEnhancer, PromptEnhancerService>();
    builder.Services.AddSingleton<IQueueService, ServiceBusQueueService>();
    builder.Services.AddHostedService<IntentModelTrainingService>();

    // Add health checks
    builder.Services.AddHealthChecks()
        .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", name: "redis")
        .AddAzureServiceBusQueue(
            builder.Configuration.GetConnectionString("ServiceBus") ?? "",
            "code-generation",
            name: "servicebus");

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