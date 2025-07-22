using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.STTService.Services;

public class AudioStorageService : IAudioStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AudioStorageService> _logger;
    private readonly string _containerName = "audio-recordings";

    public AudioStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<AudioStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> StoreAudioAsync(byte[] audioData, string requestId, string contentType)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var blobName = $"{timestamp}/{requestId}.audio";
            var blobClient = containerClient.GetBlobClient(blobName);

            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            var metadata = new Dictionary<string, string>
            {
                { "requestId", requestId },
                { "uploadedAt", DateTime.UtcNow.ToString("O") },
                { "size", audioData.Length.ToString() }
            };

            await blobClient.UploadAsync(
                new BinaryData(audioData),
                new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders,
                    Metadata = metadata
                });

            _logger.LogInformation("Stored audio for request {RequestId} as blob {BlobName}", requestId, blobName);

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store audio for request {RequestId}", requestId);
            throw;
        }
    }

    public async Task<byte[]> RetrieveAudioAsync(string audioUrl)
    {
        try
        {
            var uri = new Uri(audioUrl);
            var blobClient = new BlobClient(uri);
            
            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve audio from {AudioUrl}", audioUrl);
            throw;
        }
    }

    public async Task DeleteAudioAsync(string audioUrl)
    {
        try
        {
            var uri = new Uri(audioUrl);
            var blobClient = new BlobClient(uri);
            
            await blobClient.DeleteIfExistsAsync();
            _logger.LogInformation("Deleted audio at {AudioUrl}", audioUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete audio at {AudioUrl}", audioUrl);
            throw;
        }
    }

    public async Task<List<string>> ListAudioFilesAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var audioFiles = new List<string>();

        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            
            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                if (startDate.HasValue || endDate.HasValue)
                {
                    var blobDate = blobItem.Properties.CreatedOn?.DateTime ?? DateTime.MinValue;
                    
                    if (startDate.HasValue && blobDate < startDate.Value)
                        continue;
                    
                    if (endDate.HasValue && blobDate > endDate.Value)
                        continue;
                }

                audioFiles.Add(containerClient.GetBlobClient(blobItem.Name).Uri.ToString());
            }

            return audioFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list audio files");
            throw;
        }
    }
}