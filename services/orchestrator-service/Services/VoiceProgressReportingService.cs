using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    /// <summary>
    /// Service for reporting progress updates in a voice-friendly manner
    /// </summary>
    public interface IVoiceProgressReportingService
    {
        Task ReportProgressAsync(string taskId, string activity, double percentComplete);
        Task<string> GetVoiceProgressUpdateAsync(string taskId);
        Task SubscribeToProgressAsync(string taskId, string connectionId);
        Task UnsubscribeFromProgressAsync(string taskId, string connectionId);
    }

    public class VoiceProgressReportingService : IVoiceProgressReportingService, IHostedService
    {
        private readonly ILogger<VoiceProgressReportingService> _logger;
        private readonly IHubContext<VoiceProgressHub> _hubContext;
        private readonly ConcurrentDictionary<string, ProgressTracker> _progressTrackers = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _subscriptions = new();
        private Timer _cleanupTimer;

        public VoiceProgressReportingService(
            ILogger<VoiceProgressReportingService> logger,
            IHubContext<VoiceProgressHub> hubContext)
        {
            _logger = logger;
            _hubContext = hubContext;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Clean up old progress trackers every 5 minutes
            _cleanupTimer = new Timer(
                CleanupOldTrackers,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cleanupTimer?.Dispose();
            return Task.CompletedTask;
        }

        public async Task ReportProgressAsync(string taskId, string activity, double percentComplete)
        {
            var tracker = _progressTrackers.GetOrAdd(taskId, _ => new ProgressTracker { TaskId = taskId });
            
            tracker.CurrentActivity = activity;
            tracker.PercentComplete = percentComplete;
            tracker.LastUpdate = DateTime.UtcNow;
            tracker.Activities.Add(new ActivityRecord 
            { 
                Activity = activity, 
                Timestamp = DateTime.UtcNow,
                PercentComplete = percentComplete
            });

            // Generate voice-friendly update
            var voiceUpdate = GenerateVoiceUpdate(tracker);

            // Send to subscribed clients
            if (_subscriptions.TryGetValue(taskId, out var subscribers))
            {
                await _hubContext.Clients.Clients(subscribers.ToList())
                    .SendAsync("ProgressUpdate", new
                    {
                        taskId,
                        activity,
                        percentComplete,
                        voiceUpdate,
                        timestamp = DateTime.UtcNow
                    });
            }

            _logger.LogInformation("Progress update for task {TaskId}: {Activity} ({Percent}%)", 
                taskId, activity, percentComplete);
        }

        public async Task<string> GetVoiceProgressUpdateAsync(string taskId)
        {
            if (!_progressTrackers.TryGetValue(taskId, out var tracker))
            {
                return "No progress information available for this task.";
            }

            return GenerateVoiceUpdate(tracker);
        }

        public async Task SubscribeToProgressAsync(string taskId, string connectionId)
        {
            var subscribers = _subscriptions.GetOrAdd(taskId, _ => new HashSet<string>());
            lock (subscribers)
            {
                subscribers.Add(connectionId);
            }
            _logger.LogInformation("Client {ConnectionId} subscribed to task {TaskId}", connectionId, taskId);
        }

        public async Task UnsubscribeFromProgressAsync(string taskId, string connectionId)
        {
            if (_subscriptions.TryGetValue(taskId, out var subscribers))
            {
                lock (subscribers)
                {
                    subscribers.Remove(connectionId);
                }
            }
        }

        private string GenerateVoiceUpdate(ProgressTracker tracker)
        {
            var updates = new List<string>();

            // Current status
            if (tracker.PercentComplete < 100)
            {
                updates.Add($"I'm currently {tracker.CurrentActivity}.");
                
                if (tracker.PercentComplete > 0)
                {
                    updates.Add($"The task is about {tracker.PercentComplete:F0}% complete.");
                }

                // Estimate time remaining
                var timeRemaining = EstimateTimeRemaining(tracker);
                if (!string.IsNullOrEmpty(timeRemaining))
                {
                    updates.Add(timeRemaining);
                }
            }
            else
            {
                updates.Add("The task has been completed successfully.");
            }

            // Recent milestones
            var recentMilestones = GetRecentMilestones(tracker);
            if (recentMilestones.Any())
            {
                updates.Add($"Recent progress: {string.Join(", ", recentMilestones)}.");
            }

            return string.Join(" ", updates);
        }

        private string EstimateTimeRemaining(ProgressTracker tracker)
        {
            if (tracker.PercentComplete <= 0 || tracker.PercentComplete >= 100)
                return null;

            var elapsed = DateTime.UtcNow - tracker.StartTime;
            var estimatedTotal = elapsed.TotalMinutes / (tracker.PercentComplete / 100);
            var remaining = estimatedTotal - elapsed.TotalMinutes;

            if (remaining < 1)
                return "Almost done!";
            else if (remaining < 5)
                return $"About {remaining:F0} minutes remaining.";
            else
                return $"Approximately {remaining:F0} minutes to go.";
        }

        private List<string> GetRecentMilestones(ProgressTracker tracker)
        {
            return tracker.Activities
                .Where(a => DateTime.UtcNow - a.Timestamp < TimeSpan.FromMinutes(2))
                .Where(a => IsMilestone(a.Activity))
                .Select(a => SimplifyActivity(a.Activity))
                .Take(3)
                .ToList();
        }

        private bool IsMilestone(string activity)
        {
            var milestoneKeywords = new[] 
            { 
                "completed", "created", "generated", "built", "deployed", 
                "tested", "validated", "initialized", "configured" 
            };
            
            var lower = activity.ToLower();
            return milestoneKeywords.Any(keyword => lower.Contains(keyword));
        }

        private string SimplifyActivity(string activity)
        {
            // Simplify technical jargon for voice
            var replacements = new Dictionary<string, string>
            {
                ["npm install"] = "installed dependencies",
                ["git init"] = "initialized repository",
                ["docker build"] = "built container",
                ["webpack"] = "bundled assets",
                ["transpiling"] = "processing code",
                ["compiling"] = "building",
                ["scaffolding"] = "creating structure"
            };

            var simplified = activity;
            foreach (var replacement in replacements)
            {
                simplified = simplified.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
            }

            return simplified;
        }

        private void CleanupOldTrackers(object state)
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var toRemove = _progressTrackers
                .Where(kvp => kvp.Value.LastUpdate < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var taskId in toRemove)
            {
                _progressTrackers.TryRemove(taskId, out _);
                _subscriptions.TryRemove(taskId, out _);
            }

            if (toRemove.Any())
            {
                _logger.LogInformation("Cleaned up {Count} old progress trackers", toRemove.Count);
            }
        }

        private class ProgressTracker
        {
            public string TaskId { get; set; }
            public string CurrentActivity { get; set; } = "Initializing";
            public double PercentComplete { get; set; }
            public DateTime StartTime { get; set; } = DateTime.UtcNow;
            public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
            public List<ActivityRecord> Activities { get; set; } = new();
        }

        private class ActivityRecord
        {
            public string Activity { get; set; }
            public DateTime Timestamp { get; set; }
            public double PercentComplete { get; set; }
        }
    }

    /// <summary>
    /// SignalR hub for real-time progress updates
    /// </summary>
    public class VoiceProgressHub : Hub
    {
        private readonly IVoiceProgressReportingService _progressService;

        public VoiceProgressHub(IVoiceProgressReportingService progressService)
        {
            _progressService = progressService;
        }

        public async Task SubscribeToTask(string taskId)
        {
            await _progressService.SubscribeToProgressAsync(taskId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"task-{taskId}");
        }

        public async Task UnsubscribeFromTask(string taskId)
        {
            await _progressService.UnsubscribeFromProgressAsync(taskId, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"task-{taskId}");
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Clean up subscriptions on disconnect
            await base.OnDisconnectedAsync(exception);
        }
    }
}