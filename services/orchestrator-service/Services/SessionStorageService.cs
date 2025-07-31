using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface ISessionStorageService
    {
        Task<ConversationSession> CreateSessionAsync(string userId, string initialInput);
        Task<ConversationSession> GetSessionAsync(string sessionId);
        Task<ConversationSession> GetActiveSessionForUserAsync(string userId);
        Task<List<ConversationSession>> GetUserSessionsAsync(string userId);
        Task<ConversationSession> UpdateSessionAsync(ConversationSession session);
        Task<bool> DeleteSessionAsync(string sessionId);
        Task<ConversationSession> FindSessionByContextAsync(string userId, string userInput);
        Task AddConversationTurnAsync(string sessionId, ConversationTurn turn);
    }

    public class SessionStorageService : ISessionStorageService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<SessionStorageService> _logger;
        private readonly ConcurrentDictionary<string, List<string>> _userSessions = new();
        private readonly TimeSpan _sessionExpiration = TimeSpan.FromHours(24);

        public SessionStorageService(
            IMemoryCache cache,
            ILogger<SessionStorageService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public Task<ConversationSession> CreateSessionAsync(string userId, string initialInput)
        {
            var session = new ConversationSession
            {
                UserId = userId,
                WorkspaceId = GenerateWorkspaceId(userId),
                Title = ExtractTitle(initialInput),
                Description = initialInput
            };

            // Store session
            _cache.Set(session.SessionId, session, _sessionExpiration);

            // Track user sessions
            _userSessions.AddOrUpdate(userId, 
                new List<string> { session.SessionId },
                (key, list) => 
                {
                    list.Add(session.SessionId);
                    return list;
                });

            _logger.LogInformation("Created new session {SessionId} for user {UserId}", 
                session.SessionId, userId);

            return Task.FromResult(session);
        }

        public Task<ConversationSession> GetSessionAsync(string sessionId)
        {
            if (_cache.TryGetValue<ConversationSession>(sessionId, out var session))
            {
                return Task.FromResult(session);
            }

            return Task.FromResult<ConversationSession>(null);
        }

        public async Task<ConversationSession> GetActiveSessionForUserAsync(string userId)
        {
            if (!_userSessions.TryGetValue(userId, out var sessionIds))
            {
                return null;
            }

            // Find the most recently updated session
            ConversationSession mostRecent = null;
            DateTime mostRecentTime = DateTime.MinValue;

            foreach (var sessionId in sessionIds)
            {
                var session = await GetSessionAsync(sessionId);
                if (session != null && session.LastUpdatedAt > mostRecentTime)
                {
                    mostRecent = session;
                    mostRecentTime = session.LastUpdatedAt;
                }
            }

            return mostRecent;
        }

        public async Task<List<ConversationSession>> GetUserSessionsAsync(string userId)
        {
            if (!_userSessions.TryGetValue(userId, out var sessionIds))
            {
                return new List<ConversationSession>();
            }

            var sessions = new List<ConversationSession>();
            foreach (var sessionId in sessionIds)
            {
                var session = await GetSessionAsync(sessionId);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }

            return sessions.OrderByDescending(s => s.LastUpdatedAt).ToList();
        }

        public Task<ConversationSession> UpdateSessionAsync(ConversationSession session)
        {
            session.LastUpdatedAt = DateTime.UtcNow;
            _cache.Set(session.SessionId, session, _sessionExpiration);
            
            _logger.LogInformation("Updated session {SessionId} - Phase: {Phase}", 
                session.SessionId, session.CurrentPhase);
            
            return Task.FromResult(session);
        }

        public Task<bool> DeleteSessionAsync(string sessionId)
        {
            _cache.Remove(sessionId);
            
            // Remove from user sessions
            foreach (var userSessions in _userSessions.Values)
            {
                userSessions.Remove(sessionId);
            }

            return Task.FromResult(true);
        }

        public async Task<ConversationSession> FindSessionByContextAsync(string userId, string userInput)
        {
            var sessions = await GetUserSessionsAsync(userId);
            
            // Look for keywords that might reference existing projects
            var lowerInput = userInput.ToLower();
            
            foreach (var session in sessions)
            {
                // Check if user references the session title or key terms
                if (!string.IsNullOrEmpty(session.Title) && 
                    lowerInput.Contains(session.Title.ToLower()))
                {
                    return session;
                }

                // Check for worker references
                if (!string.IsNullOrEmpty(session.AssignedWorkerId) &&
                    (lowerInput.Contains("continue") || 
                     lowerInput.Contains("status") || 
                     lowerInput.Contains("check on")))
                {
                    return session;
                }

                // Check for technology mentions from technical context
                if (session.TechnicalContext?.TechnologyStack != null)
                {
                    foreach (var tech in session.TechnicalContext.TechnologyStack)
                    {
                        if (lowerInput.Contains(tech.ToLower()))
                        {
                            return session;
                        }
                    }
                }
            }

            return null;
        }

        public async Task AddConversationTurnAsync(string sessionId, ConversationTurn turn)
        {
            var session = await GetSessionAsync(sessionId);
            if (session != null)
            {
                session.History.Add(turn);
                await UpdateSessionAsync(session);
            }
        }

        private string GenerateWorkspaceId(string userId)
        {
            // Generate a workspace ID based on user and timestamp
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            return $"{userId}-{timestamp}";
        }

        private string ExtractTitle(string input)
        {
            // Extract a meaningful title from the initial input
            var words = input.Split(' ')
                .Where(w => w.Length > 3)
                .Take(5);
            
            return string.Join(" ", words);
        }
    }
}