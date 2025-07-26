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
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.AspNetCore.Authentication;

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

    // Add authentication - support Azure AD, custom JWT, and simple tokens
    var authBuilder = builder.Services.AddAuthentication("Multiple");
    
    // Add Azure AD authentication
    authBuilder.AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    
    // Add custom JWT authentication
    authBuilder.AddJwtBearer("CustomJwt", options =>
    {
        var secretKey = builder.Configuration["Authentication:JwtSecret"] ?? "VoiceCodeDevelopmentSecretKey123!ThisShouldBeInKeyVault";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(secretKey)),
            ValidateIssuer = true,
            ValidIssuer = "voicecode-auth",
            ValidateAudience = true,
            ValidAudience = "voicecode-api",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
    
    // Add simple token authentication
    authBuilder.AddScheme<SimpleTokenAuthOptions, SimpleTokenAuthHandler>("SimpleToken", null);
    
    // Configure authorization with a policy scheme selector
    builder.Services.AddAuthentication()
        .AddPolicyScheme("Multiple", "Multiple", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ") && authHeader.Length > 50)
                {
                    var token = authHeader.Substring("Bearer ".Length);
                    // Check if it's a proper JWT with 3 parts
                    var parts = token.Split('.');
                    if (parts.Length == 3)
                    {
                        // Try Azure AD first, then CustomJwt
                        return "Bearer";
                    }
                }
                // Simple token
                return "SimpleToken";
            };
        });
    
    // Configure authorization to accept any auth scheme
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes("Bearer", "CustomJwt", "SimpleToken", "Multiple")
            .RequireAuthenticatedUser()
            .Build();
    });

    // Add Application Insights
    builder.Services.AddApplicationInsightsTelemetry();
    
    // Add HttpClient for proxy requests
    builder.Services.AddHttpClient();

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
    builder.Services.AddSingleton<VoiceCode.RouterService.Services.IQueueService, ServiceBusQueueService>();

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