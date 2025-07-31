using Serilog;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Services;
using VoiceCode.WorkerService.Models;
using Microsoft.Extensions.Azure;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog early for logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "WorkerService")
    .CreateBootstrapLogger();

// Add Azure Key Vault configuration
if (!builder.Environment.IsDevelopment())
{
    var keyVaultName = builder.Configuration["KeyVaultName"];
    Log.Information($"Environment: {builder.Environment.EnvironmentName}, KeyVaultName: {keyVaultName ?? "not set"}");
    
    if (!string.IsNullOrEmpty(keyVaultName))
    {
        try
        {
            var keyVaultEndpoint = new Uri($"https://{keyVaultName}.vault.azure.net/");
            var credential = new DefaultAzureCredential();
            builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, credential);
            Log.Information($"Successfully configured Key Vault: {keyVaultName}");
            
            // Log available configuration sources
            Log.Information("Configuration sources after Key Vault: {Sources}", 
                string.Join(", ", builder.Configuration.Sources.Select(s => s.GetType().Name)));
            
            // Test if we can read the Claude API key from configuration
            var testKey = builder.Configuration["ClaudeApiKey"];
            Log.Information("ClaudeApiKey from configuration exists: {Exists}, Length: {Length}", 
                !string.IsNullOrEmpty(testKey), testKey?.Length ?? 0);
            
            // Also check environment variables
            var envApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            Log.Information("ANTHROPIC_API_KEY from environment exists: {Exists}, Length: {Length}", 
                !string.IsNullOrEmpty(envApiKey), envApiKey?.Length ?? 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to configure Key Vault: {keyVaultName}");
        }
    }
    else
    {
        Log.Warning("KeyVaultName not configured - Key Vault integration disabled");
    }
}

// Reconfigure Serilog with full configuration
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "WorkerService")
    .WriteTo.Console()
    .WriteTo.ApplicationInsights(
        builder.Configuration["ApplicationInsights:ConnectionString"] ?? "",
        TelemetryConverter.Traces)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "VoiceCode Worker Service", Version = "v1" });
});

// Configure options
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));

// Register Azure clients
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]);
});

// Register services
builder.Services.AddSingleton<IMcpClientService, McpClientService>();
builder.Services.AddHttpClient<IClaudeCodeSidecarClient, ClaudeCodeSidecarClient>();
builder.Services.AddScoped<IRepositoryAnalyzer, RepositoryAnalyzer>();
builder.Services.AddScoped<IClaudeCodeWorkerService, ClaudeCodeWorkerService>();
builder.Services.AddHostedService<WorkerQueueProcessorService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

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

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Skip MCP connection on startup - let it connect when needed
Log.Information("Skipping MCP connection on startup - will connect on demand");

Log.Information("VoiceCode Worker Service started");

app.Run();