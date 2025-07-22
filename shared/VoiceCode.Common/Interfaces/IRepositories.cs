using VoiceCode.Common.Models;

namespace VoiceCode.Common.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string userId);
    Task<User?> GetByEmailAsync(string email);
    Task<User> CreateAsync(User user);
    Task<User> UpdateAsync(User user);
    Task DeleteAsync(string userId);
    Task<PagedResponse<User>> GetAllAsync(int pageNumber, int pageSize);
}

public interface IContextRepository
{
    Task<UserContext?> GetContextAsync(string userId, string sessionId);
    Task SaveContextAsync(UserContext context);
    Task<List<ContextEntry>> GetRecentInteractionsAsync(string userId, int count);
    Task CleanupOldContextAsync(DateTime before);
}

public interface ICodeHistoryRepository
{
    Task<CodeGenerationResponse?> GetByIdAsync(string id);
    Task<List<CodeGenerationResponse>> GetByUserAsync(string userId, int count);
    Task SaveAsync(CodeGenerationResponse response);
    Task<UsageMetrics> GetUsageMetricsAsync(string userId, DateTime startDate, DateTime endDate);
}

public interface IAuditRepository
{
    Task LogAsync(AuditLog auditLog);
    Task<List<AuditLog>> QueryAsync(string userId, DateTime startDate, DateTime endDate);
    Task<List<AuditLog>> GetByResourceAsync(string resourceType, string resourceId);
}