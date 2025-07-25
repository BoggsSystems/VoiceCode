using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add session support for worker context tracking
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Configure Service Bus
builder.Services.AddSingleton<ServiceBusClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var fullyQualifiedNamespace = config["ServiceBus:FullyQualifiedNamespace"];
    return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("*")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseSession();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TaskProgressHub>("/hubs/progress");

app.Run();

// SignalR Hub for real-time updates
public class TaskProgressHub : Hub
{
    public async Task SendTaskUpdate(string taskId, string status, string message)
    {
        await Clients.All.SendAsync("TaskUpdate", taskId, status, message);
    }
}