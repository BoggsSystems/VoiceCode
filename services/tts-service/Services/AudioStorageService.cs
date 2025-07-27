using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using VoiceCode.TTSService.Services.Interfaces;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.TTSService.Services;

public class AudioStorageService : Interfaces.IAudioStorageService, VoiceCode.Common.Interfaces.IAudioStorageService
{
    private readonly ILogger<AudioStorageService> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName = "audio-files";

    public AudioStorageService(
        ILogger<AudioStorageService> logger,
        BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _blobServiceClient = blobServiceClient;
    }

    public async Task<string> StoreAudioAsync(byte[] audioData, string sessionId, string fileExtension)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var fileName = $"{sessionId}/{Guid.NewGuid()}.{fileExtension}";
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(audioData);
            await blobClient.UploadAsync(stream, new BlobHttpHeaders
            {
                ContentType = GetContentType(fileExtension)
            });

            _logger.LogInformation("Stored audio file {FileName} ({Size} bytes)", fileName, audioData.Length);
            
            // Generate SAS URL with 24-hour expiry
            var sasUrl = GenerateSasUrl(blobClient);
            _logger.LogInformation("Generated SAS URL for audio file {FileName}", fileName);
            
            return sasUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing audio file");
            throw;
        }
    }

    public async Task<byte[]?> GetAudioAsync(string audioUrl)
    {
        try
        {
            var uri = new Uri(audioUrl);
            var blobName = uri.AbsolutePath.TrimStart('/').Substring(_containerName.Length + 1);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                return null;
            }

            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audio file from {Url}", audioUrl);
            return null;
        }
    }

    async Task<bool> Interfaces.IAudioStorageService.DeleteAudioAsync(string audioUrl)
    {
        try
        {
            var uri = new Uri(audioUrl);
            var blobName = uri.AbsolutePath.TrimStart('/').Substring(_containerName.Length + 1);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var response = await blobClient.DeleteIfExistsAsync();
            _logger.LogInformation("Deleted audio file {Url}: {Success}", audioUrl, response.Value);
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting audio file {Url}", audioUrl);
            return false;
        }
    }

    public async Task<List<string>> ListAudioFilesAsync(string sessionId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var prefix = $"{sessionId}/";
            var audioFiles = new List<string>();

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var sasUrl = GenerateSasUrl(blobClient);
                audioFiles.Add(sasUrl);
            }

            return audioFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing audio files for session {SessionId}", sessionId);
            return new List<string>();
        }
    }

    // Implement Common.Interfaces.IAudioStorageService with different signature
    async Task<string> VoiceCode.Common.Interfaces.IAudioStorageService.StoreAudioAsync(byte[] audioData, string requestId, string contentType)
    {
        var extension = contentType switch
        {
            "audio/mpeg" => ".mp3",
            "audio/opus" => ".opus",
            "audio/ogg" => ".ogg",
            "audio/wav" => ".wav",
            _ => ".mp3"
        };
        return await StoreAudioAsync(audioData, requestId, extension);
    }

    async Task<byte[]> VoiceCode.Common.Interfaces.IAudioStorageService.RetrieveAudioAsync(string audioUrl)
    {
        return await GetAudioAsync(audioUrl) ?? Array.Empty<byte>();
    }

    async Task VoiceCode.Common.Interfaces.IAudioStorageService.DeleteAudioAsync(string audioUrl)
    {
        await ((Interfaces.IAudioStorageService)this).DeleteAudioAsync(audioUrl);
    }

    async Task<List<string>> VoiceCode.Common.Interfaces.IAudioStorageService.ListAudioFilesAsync(DateTime? startDate, DateTime? endDate)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var audioFiles = new List<string>();

            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                if (startDate.HasValue && blobItem.Properties.CreatedOn < startDate.Value)
                    continue;
                if (endDate.HasValue && blobItem.Properties.CreatedOn > endDate.Value)
                    continue;
                    
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var sasUrl = GenerateSasUrl(blobClient);
                audioFiles.Add(sasUrl);
            }

            return audioFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing audio files");
            return new List<string>();
        }
    }

    private string GetContentType(string fileExtension)
    {
        return fileExtension.ToLower() switch
        {
            ".mp3" => "audio/mpeg",
            ".opus" => "audio/opus",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".pcm" => "audio/pcm",
            _ => "application/octet-stream"
        };
    }

    private string GenerateSasUrl(BlobClient blobClient)
    {
        // Check if BlobClient can generate SAS
        if (!blobClient.CanGenerateSasUri)
        {
            _logger.LogWarning("BlobClient cannot generate SAS URI. Returning direct URL instead.");
            return blobClient.Uri.ToString();
        }

        // Create a SAS token that's valid for 24 hours
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b", // b for blob
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5), // Account for clock skew
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(24),
        };

        // Specify read permissions for the SAS
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        // Generate the SAS URI
        return blobClient.GenerateSasUri(sasBuilder).ToString();
    }
}