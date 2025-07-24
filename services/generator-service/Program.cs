using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Identity.Web;
using Serilog;
using VoiceCode.GeneratorService.Configuration;
using VoiceCode.GeneratorService.Middleware;
using VoiceCode.GeneratorService.Services;
using VoiceCode.GeneratorService.Processors;
using VoiceCode.Common.Interfaces;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using StackExchange.Redis;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "GeneratorService")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VoiceCode Generator Service");

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
            Title = "VoiceCode Generator Service", 
            Version = "v1",
            Description = "Code generation orchestration service for VoiceCode"
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

    // Add Service Bus client
    builder.Services.AddSingleton<ServiceBusClient>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        return new ServiceBusClient(configuration.GetConnectionString("ServiceBus"));
    });

    // Add configuration
    builder.Services.Configure<GeneratorOptions>(builder.Configuration.GetSection("Generator"));
    builder.Services.Configure<TemplateOptions>(builder.Configuration.GetSection("Templates"));

    // Add services
    builder.Services.AddScoped<ICodeGenerator, CodeGeneratorService>();
    builder.Services.AddSingleton<ITemplateEngine, TemplateEngineService>();
    builder.Services.AddSingleton<ICodeValidator, CodeValidatorService>();
    builder.Services.AddSingleton<ICodeFormatter, CodeFormatterService>();
    builder.Services.AddSingleton<IFileOrganizer, FileOrganizerService>();
    
    // Add processors
    builder.Services.AddScoped<ILanguageProcessor, CSharpProcessor>();
    builder.Services.AddScoped<ILanguageProcessor, TypeScriptProcessor>();
    builder.Services.AddScoped<ILanguageProcessor, PythonProcessor>();
    builder.Services.AddScoped<ILanguageProcessor, JavaProcessor>();
    builder.Services.AddScoped<IProcessorFactory, ProcessorFactory>();

    // Add HttpClient for Claude service
    builder.Services.AddHttpClient<IClaudeService, ClaudeProxyService>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["ClaudeService:BaseUrl"] ?? "http://localhost:5002");
    });

    // Add hosted services
    builder.Services.AddHostedService<QueueProcessorService>();

    // Add health checks
    var healthChecks = builder.Services.AddHealthChecks();
    
    if (useRedis)
    {
        healthChecks.AddRedis(redisConnectionString!, name: "redis");
    }

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