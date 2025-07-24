using Serilog;
using VoiceCode.OrchestratorService.Configuration;
using VoiceCode.OrchestratorService.Services;
using VoiceCode.Common.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/orchestrator-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure options
builder.Services.Configure<OrchestrationOptions>(
    builder.Configuration.GetSection("Orchestration"));
builder.Services.Configure<ServiceEndpoints>(
    builder.Configuration.GetSection("ServiceEndpoints"));

// Register HTTP clients
builder.Services.AddHttpClient("ClaudeService", client =>
{
    var endpoints = builder.Configuration.GetSection("ServiceEndpoints").Get<ServiceEndpoints>();
    client.BaseAddress = new Uri(endpoints?.ClaudeService ?? "http://localhost:5005");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Register services
builder.Services.AddSingleton<ResponseTranslationService>();
builder.Services.AddSingleton<IWorkerManagementService, WorkerManagementService>();

// Register HTTP client factory
builder.Services.AddHttpClient();

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

Log.Information("Orchestrator Service starting on port {Port}", 
    builder.Configuration["ASPNETCORE_URLS"] ?? "5010");

app.Run();