using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Identity.Web;
using Serilog;
using VoiceCode.ClaudeService.Configuration;
using VoiceCode.ClaudeService.Middleware;
using VoiceCode.ClaudeService.Services;
using VoiceCode.ClaudeService.HealthChecks;
using VoiceCode.Common.Interfaces;
using Azure.Identity;
using StackExchange.Redis;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(TelemetryConfiguration.CreateDefault(), TelemetryConverter.Traces)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ClaudeService")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VoiceCode Claude Service");

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
            Title = "VoiceCode Claude Service", 
            Version = "v1",
            Description = "Claude AI integration service for VoiceCode"
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

    // Add Claude configuration
    builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection("Claude"));
    builder.Services.Configure<ResponseInterpreterOptions>(builder.Configuration.GetSection("ResponseInterpreter"));

    // Add HttpClient for Claude service
    builder.Services.AddHttpClient<IClaudeService, ClaudeApiService>();

    // Add services
    builder.Services.AddSingleton<IPromptTemplateService, PromptTemplateService>();
    builder.Services.AddSingleton<ITokenCounterService, TokenCounterService>();
    builder.Services.AddScoped<IResponseInterpreterService, ResponseInterpreterService>();
    
    // Add HttpClient for health check
    builder.Services.AddHttpClient<ClaudeHealthCheck>();

    // Add health checks
    var healthChecks = builder.Services.AddHealthChecks();
    
    if (useRedis)
    {
        healthChecks.AddRedis(redisConnectionString!, name: "redis");
    }
    
    healthChecks.AddCheck<ClaudeHealthCheck>("claude_api");

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
    app.UseMiddleware<RateLimitingMiddleware>();

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