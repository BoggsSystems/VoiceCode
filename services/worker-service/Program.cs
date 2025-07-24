using Serilog;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
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

// Register services
builder.Services.AddSingleton<IMcpClientService, McpClientService>();
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

// Initialize MCP connection on startup
try
{
    using var scope = app.Services.CreateScope();
    var mcpClient = scope.ServiceProvider.GetRequiredService<IMcpClientService>();
    var connected = await mcpClient.ConnectAsync();
    
    if (connected)
    {
        Log.Information("MCP connection established on startup");
    }
    else
    {
        Log.Warning("Failed to establish MCP connection on startup");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "Error during startup MCP connection");
}

Log.Information("VoiceCode Worker Service started");

app.Run();