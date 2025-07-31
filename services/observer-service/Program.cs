using Serilog;
using VoiceCode.ObserverService.Services;
using VoiceCode.ObserverService.Configuration;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ObserverService")
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
    c.SwaggerDoc("v1", new() { Title = "VoiceCode Observer Service", Version = "v1" });
});

// Configure options
builder.Services.Configure<ObserverOptions>(builder.Configuration.GetSection(ObserverOptions.SectionName));
builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection(ServiceBusOptions.SectionName));
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection(OpenAIOptions.SectionName));

// Register Azure clients
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]);
});

// Register services
builder.Services.AddSingleton<IStreamProcessor, StreamProcessor>();
builder.Services.AddSingleton<INarrationGenerator, NarrationGenerator>();
builder.Services.AddSingleton<IServiceBusPublisher, ServiceBusPublisher>();
builder.Services.AddHttpClient<IOpenAIService, OpenAIService>();
builder.Services.AddHostedService<SdkStreamProcessorService>();

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

Log.Information("VoiceCode Observer Service started");

app.Run();