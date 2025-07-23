using VoiceCode.Common.Models;

namespace VoiceCode.Common.Interfaces;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password);
    Task<string> GenerateTokenAsync(User user);
    Task<bool> ValidateTokenAsync(string token);
    Task<User?> GetUserFromTokenAsync(string token);
    Task RevokeTokenAsync(string token);
}

public interface IStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
    Task<Stream> DownloadFileAsync(string fileUrl);
    Task DeleteFileAsync(string fileUrl);
    Task<bool> FileExistsAsync(string fileUrl);
    Task<string> GeneratePresignedUrlAsync(string fileUrl, TimeSpan expiration);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    Task<bool> ExistsAsync(string key);
    Task RemoveAsync(string key);
    Task<bool> DeleteAsync(string key);
    Task<List<string>> GetKeysAsync(string pattern);
    Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null);
    Task<bool> SetAddAsync(string key, string value, TimeSpan? expiry = null);
    Task<string[]> SetMembersAsync(string key);
    Task<bool> LockAsync(string key, TimeSpan duration);
    Task<bool> LockAsync(string key, string value, TimeSpan expiry);
    Task UnlockAsync(string key);
    Task<bool> UnlockAsync(string key, string value);
}

public interface IQueueService
{
    Task SendMessageAsync<T>(string queueName, T message, Dictionary<string, object>? properties = null) where T : class;
    Task<ServiceBusMessage<T>?> ReceiveMessageAsync<T>(string queueName, TimeSpan? timeout = null) where T : class;
    Task CompleteMessageAsync(string queueName, string messageId);
    Task AbandonMessageAsync(string queueName, string messageId);
    Task DeadLetterMessageAsync(string queueName, string messageId, string reason);
}

// Supporting types
public class AuthenticationResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public User? User { get; set; }
    public string? Error { get; set; }
}