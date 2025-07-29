using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;

namespace VoiceCode.Common.Services;

public class AudioResponseTableService : IAudioResponseTableService
{
    private readonly TableClient _tableClient;
    private readonly ILogger<AudioResponseTableService> _logger;

    public AudioResponseTableService(IConfiguration configuration, ILogger<AudioResponseTableService> logger)
    {
        _logger = logger;
        
        var connectionString = configuration.GetConnectionString("AzureStorage");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Azure Storage connection string not configured");
        }

        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient("AudioResponses");
    }

    public async Task StoreAudioResponseAsync(string taskId, string sessionId, string audioUrl, string text, double duration)
    {
        try
        {
            var entity = new AudioResponseEntity(taskId, sessionId, audioUrl, text, duration);
            await _tableClient.AddEntityAsync(entity);
            
            _logger.LogInformation("Stored audio response in table for task {TaskId}, session {SessionId}", 
                taskId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store audio response for task {TaskId}", taskId);
            throw;
        }
    }

    public async Task<AudioResponseEntity?> GetAudioResponseAsync(string taskId)
    {
        try
        {
            var response = await _tableClient.GetEntityIfExistsAsync<AudioResponseEntity>("AudioResponses", taskId);
            
            if (response.HasValue)
            {
                _logger.LogDebug("Retrieved audio response for task {TaskId}", taskId);
                return response.Value;
            }
            
            _logger.LogDebug("No audio response found for task {TaskId}", taskId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve audio response for task {TaskId}", taskId);
            return null;
        }
    }

    public async Task<bool> AudioResponseExistsAsync(string taskId)
    {
        try
        {
            var response = await _tableClient.GetEntityIfExistsAsync<AudioResponseEntity>("AudioResponses", taskId);
            return response.HasValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check audio response existence for task {TaskId}", taskId);
            return false;
        }
    }
}