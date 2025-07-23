using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using VoiceCode.DispatcherService.Configuration;

namespace VoiceCode.DispatcherService.Services;

public interface ISessionManager
{
    Task<UserSession> CreateSessionAsync(string userId, string connectionId);
    Task<UserSession?> GetSessionAsync(string sessionId);
    Task<UserSession?> GetSessionByConnectionIdAsync(string connectionId);
    Task<List<UserSession>> GetActiveSessionsAsync();
    Task UpdateSessionActivityAsync(string sessionId);
    Task UpdateSessionContextAsync(string sessionId, Dictionary<string, object> context);
    Task EndSessionAsync(string sessionId);
    Task<int> GetActiveSessionCountAsync();
}

public class SessionManager : ISessionManager
{
    private readonly ILogger<SessionManager> _logger;
    private readonly IOptions<DispatcherOptions> _options;
    private readonly ConcurrentDictionary<string, UserSession> _sessions;
    private readonly ConcurrentDictionary<string, string> _connectionToSession;

    public SessionManager(
        ILogger<SessionManager> logger,
        IOptions<DispatcherOptions> options)
    {
        _logger = logger;
        _options = options;
        _sessions = new ConcurrentDictionary<string, UserSession>();
        _connectionToSession = new ConcurrentDictionary<string, string>();
    }

    public Task<UserSession> CreateSessionAsync(string userId, string connectionId)
    {
        var session = new UserSession
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ConnectionId = connectionId,
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            State = SessionState.Active,
            Context = new UserContext
            {
                UserId = userId,
                Preferences = new UserPreferences(),
                History = new List<ContextEntry>()
            }
        };

        if (_sessions.Count >= _options.Value.MaxConcurrentSessions)
        {
            throw new InvalidOperationException("Maximum concurrent sessions reached");
        }

        _sessions[session.Id] = session;
        _connectionToSession[connectionId] = session.Id;

        _logger.LogInformation("Created session {SessionId} for user {UserId}", session.Id, userId);
        return Task.FromResult(session);
    }

    public Task<UserSession?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<UserSession?> GetSessionByConnectionIdAsync(string connectionId)
    {
        if (_connectionToSession.TryGetValue(connectionId, out var sessionId))
        {
            return GetSessionAsync(sessionId);
        }
        return Task.FromResult<UserSession?>(null);
    }

    public Task<List<UserSession>> GetActiveSessionsAsync()
    {
        var activeSessions = _sessions.Values
            .Where(s => s.State == SessionState.Active)
            .ToList();

        return Task.FromResult(activeSessions);
    }

    public Task UpdateSessionActivityAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.LastActivity = DateTime.UtcNow;
            session.MessageCount++;

            if (session.MessageCount > _options.Value.MaxMessagesPerSession)
            {
                _logger.LogWarning("Session {SessionId} exceeded message limit", sessionId);
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateSessionContextAsync(string sessionId, Dictionary<string, object> context)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            // Merge context
            foreach (var kvp in context)
            {
                if (kvp.Key == "preferences" && kvp.Value is Dictionary<string, object> prefs)
                {
                    // Update preferences
                    if (prefs.TryGetValue("personality", out var personality))
                    {
                        session.Context.Preferences.Personality = Enum.Parse<PersonalityProfile>(personality.ToString()!);
                    }
                    if (prefs.TryGetValue("outputFormat", out var format))
                    {
                        session.Context.Preferences.OutputFormat = format.ToString();
                    }
                }
                else if (kvp.Key == "currentProject" && kvp.Value is Dictionary<string, object> project)
                {
                    session.Context.CurrentProject = new ProjectContext
                    {
                        Name = project.GetValueOrDefault("name")?.ToString() ?? "",
                        Language = project.GetValueOrDefault("language")?.ToString() ?? "",
                        Framework = project.GetValueOrDefault("framework")?.ToString(),
                        Path = project.GetValueOrDefault("path")?.ToString() ?? ""
                    };
                }
            }

            _logger.LogInformation("Updated context for session {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task EndSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.State = SessionState.Ended;
            session.EndTime = DateTime.UtcNow;
            
            if (!string.IsNullOrEmpty(session.ConnectionId))
            {
                _connectionToSession.TryRemove(session.ConnectionId, out _);
            }

            _logger.LogInformation("Ended session {SessionId} for user {UserId}", sessionId, session.UserId);
        }

        return Task.CompletedTask;
    }

    public Task<int> GetActiveSessionCountAsync()
    {
        var count = _sessions.Count(s => s.Value.State == SessionState.Active);
        return Task.FromResult(count);
    }

    public void CleanupInactiveSessions(TimeSpan inactivityThreshold)
    {
        var cutoffTime = DateTime.UtcNow - inactivityThreshold;
        var inactiveSessions = _sessions.Values
            .Where(s => s.LastActivity < cutoffTime && s.State == SessionState.Active)
            .ToList();

        foreach (var session in inactiveSessions)
        {
            _ = EndSessionAsync(session.Id);
            _logger.LogInformation("Cleaned up inactive session {SessionId}", session.Id);
        }
    }
}