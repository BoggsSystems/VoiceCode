using VoiceCode.VoiceIntelligenceService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add application services
builder.Services.AddSingleton<IOpenAISynthesisService, OpenAISynthesisService>();
builder.Services.AddSingleton<ITTSQueueService, TTSQueueService>();
builder.Services.AddHostedService<ResultProcessorService>();

// Configure OpenAI settings
builder.Configuration.AddEnvironmentVariables();

// Explicitly set configuration values from environment variables
var serviceBusConnectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

if (!string.IsNullOrEmpty(serviceBusConnectionString))
{
    builder.Configuration["ServiceBus:ConnectionString"] = serviceBusConnectionString;
}

if (!string.IsNullOrEmpty(openAiApiKey))
{
    builder.Configuration["OpenAI:ApiKey"] = openAiApiKey;
}

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health");

// Log startup
app.Logger.LogInformation("Voice Intelligence Service starting...");
app.Logger.LogInformation("OpenAI API Key configured: {HasKey}", 
    !string.IsNullOrEmpty(app.Configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")));
app.Logger.LogInformation("Service Bus configured: {HasConnection}", 
    !string.IsNullOrEmpty(app.Configuration["ServiceBus:ConnectionString"] ?? Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING")));
app.Logger.LogInformation("Service Bus Connection String from env: {HasEnv}",
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING")));

app.Run();