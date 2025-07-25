using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface ISimplifiedVoiceTaskService
    {
        Task<VoiceTask> CreateTaskFromVoiceAsync(string voicePrompt, string sessionId);
        Task<VoiceTask> AssignTaskToWorkerAsync(VoiceTask task);
        Task<VoiceTaskResult> ExecuteTaskAsync(VoiceTask task);
        Task UpdateTaskProgressAsync(string taskId, TaskProgress progress);
        Task<TaskProgress> GetTaskProgressAsync(string taskId);
        Task<string> GenerateVoiceResponseAsync(VoiceTaskResult result);
    }

    public class SimplifiedVoiceTaskService : ISimplifiedVoiceTaskService
    {
        private readonly ILogger<SimplifiedVoiceTaskService> _logger;
        private readonly IWorkerManagementService _workerManagement;
        private readonly IWorkerPoolService _workerPool;
        private readonly IRepoAwareRoutingService _repoRouting;
        private readonly Dictionary<string, VoiceTask> _activeTasks = new();
        private readonly Dictionary<string, TaskProgress> _taskProgress = new();

        public SimplifiedVoiceTaskService(
            ILogger<SimplifiedVoiceTaskService> logger,
            IWorkerManagementService workerManagement,
            IWorkerPoolService workerPool,
            IRepoAwareRoutingService repoRouting)
        {
            _logger = logger;
            _workerManagement = workerManagement;
            _workerPool = workerPool;
            _repoRouting = repoRouting;
        }

        public async Task<VoiceTask> CreateTaskFromVoiceAsync(string voicePrompt, string sessionId)
        {
            _logger.LogInformation("Creating task from voice prompt: {Prompt}", voicePrompt);

            var task = new VoiceTask
            {
                SessionId = sessionId,
                VoicePrompt = voicePrompt,
                Status = TaskStatus.Pending,
                Metadata = ExtractBasicMetadata(voicePrompt)
            };

            _activeTasks[task.TaskId] = task;

            // Let Claude Code handle all the complex planning
            _logger.LogInformation("Created task {TaskId} - delegating planning to Claude Code", task.TaskId);

            return task;
        }

        private Dictionary<string, string> ExtractBasicMetadata(string prompt)
        {
            var metadata = new Dictionary<string, string>();
            var lowerPrompt = prompt.ToLower();

            // Extract basic hints for worker selection (optional)
            if (lowerPrompt.Contains("test") || lowerPrompt.Contains("testing"))
            {
                metadata["taskType"] = "testing";
            }
            else if (lowerPrompt.Contains("frontend") || lowerPrompt.Contains("ui") || lowerPrompt.Contains("react"))
            {
                metadata["taskType"] = "frontend";
            }
            else if (lowerPrompt.Contains("api") || lowerPrompt.Contains("backend") || lowerPrompt.Contains("database"))
            {
                metadata["taskType"] = "backend";
            }
            else
            {
                metadata["taskType"] = "general";
            }

            // Extract language hints
            var languages = new[] { "python", "javascript", "typescript", "csharp", "java", "go" };
            foreach (var lang in languages)
            {
                if (lowerPrompt.Contains(lang))
                {
                    metadata["preferredLanguage"] = lang;
                    break;
                }
            }

            return metadata;
        }

        public async Task<VoiceTask> AssignTaskToWorkerAsync(VoiceTask task)
        {
            _logger.LogInformation("Assigning task {TaskId} to worker", task.TaskId);

            try
            {
                // First try repository-aware routing based on keywords
                var selectedWorkerId = await _repoRouting.SelectWorkerByKeywordsAsync(task.VoicePrompt);
                
                if (!string.IsNullOrEmpty(selectedWorkerId))
                {
                    _logger.LogInformation("Repository-aware routing selected worker {WorkerId} for task {TaskId}", 
                        selectedWorkerId, task.TaskId);
                    
                    // Get repository configuration for context
                    var repoConfig = await _repoRouting.GetRepoConfigForWorkerAsync(selectedWorkerId);
                    if (repoConfig != null)
                    {
                        task.Metadata["repository"] = repoConfig.Repo;
                        task.Metadata["repositoryName"] = repoConfig.Name;
                    }
                    
                    task.AssignedWorkerId = selectedWorkerId;
                    task.Status = TaskStatus.Assigned;
                    
                    // Note: Direct assignment since we're using specific worker containers
                    _logger.LogInformation("Task {TaskId} assigned to repository-specific worker {WorkerId}", 
                        task.TaskId, selectedWorkerId);
                    
                    return task;
                }
                
                // Fallback to general pool selection if no keyword match
                _logger.LogInformation("No keyword match found, using general worker pool");
                
                // Create a simple Claude Code task
                var claudeTask = new ClaudeCodeTask
                {
                    Id = task.TaskId,
                    Type = "voice_task",
                    Prompt = task.VoicePrompt,
                    Context = task.Metadata
                };

                // Select worker using the pool service
                var worker = await _workerPool.SelectWorkerForTaskAsync(claudeTask);
                if (worker == null)
                {
                    throw new InvalidOperationException("No available workers");
                }

                task.AssignedWorkerId = worker.WorkerId;
                task.Status = TaskStatus.Assigned;

                await _workerPool.AssignTaskToWorkerAsync(task.TaskId, worker.WorkerId);

                _logger.LogInformation("Task {TaskId} assigned to worker {WorkerId}", 
                    task.TaskId, worker.WorkerId);

                return task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to assign task {TaskId}", task.TaskId);
                task.Status = TaskStatus.Failed;
                throw;
            }
        }

        public async Task<VoiceTaskResult> ExecuteTaskAsync(VoiceTask task)
        {
            _logger.LogInformation("Executing task {TaskId} on worker {WorkerId}", 
                task.TaskId, task.AssignedWorkerId);

            task.Status = TaskStatus.InProgress;
            var startTime = DateTime.UtcNow;

            try
            {
                // Create worker task with full voice prompt
                var workerTask = new WorkerTask
                {
                    Id = task.TaskId,
                    Type = "claude_code_execution",
                    Description = task.VoicePrompt,
                    Input = task.VoicePrompt,
                    Context = task.Metadata,
                    WorkspaceId = task.SessionId
                };

                // Submit to worker - Claude Code will handle all planning
                var taskId = await _workerManagement.SubmitTaskAsync(workerTask);

                // Wait for result (with generous timeout for complex tasks)
                var result = await _workerManagement.GetTaskResultAsync(
                    taskId, 
                    TimeSpan.FromMinutes(30));

                if (result == null)
                {
                    throw new TimeoutException("Task execution timed out");
                }

                // Convert to voice-friendly result
                var voiceResult = new VoiceTaskResult
                {
                    Success = result.Success,
                    Summary = result.Summary,
                    Duration = DateTime.UtcNow - startTime,
                    Error = result.Error
                };

                // Extract file information
                foreach (var file in result.FileOperations)
                {
                    if (file.OperationType == "create")
                        voiceResult.FilesCreated.Add(file.FilePath);
                    else if (file.OperationType == "modify")
                        voiceResult.FilesModified.Add(file.FilePath);
                }

                // Extract key outcomes
                if (result.Metadata != null)
                {
                    foreach (var kvp in result.Metadata)
                    {
                        voiceResult.KeyOutcomes[kvp.Key] = kvp.Value?.ToString() ?? "";
                    }
                }

                // Generate voice-friendly response
                voiceResult.VoiceFriendlyResponse = await GenerateVoiceResponseAsync(voiceResult);

                task.Result = voiceResult;
                task.Status = TaskStatus.Completed;

                // Release worker
                await _workerPool.ReleaseTaskFromWorkerAsync(task.TaskId, task.AssignedWorkerId);

                _logger.LogInformation("Task {TaskId} completed successfully", task.TaskId);

                return voiceResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute task {TaskId}", task.TaskId);
                
                task.Status = TaskStatus.Failed;
                task.Result = new VoiceTaskResult
                {
                    Success = false,
                    Error = ex.Message,
                    Duration = DateTime.UtcNow - startTime,
                    VoiceFriendlyResponse = $"I encountered an error: {ex.Message}. Would you like me to try again?"
                };

                // Release worker
                if (!string.IsNullOrEmpty(task.AssignedWorkerId))
                {
                    await _workerPool.ReleaseTaskFromWorkerAsync(task.TaskId, task.AssignedWorkerId);
                }

                throw;
            }
        }

        public async Task UpdateTaskProgressAsync(string taskId, TaskProgress progress)
        {
            _taskProgress[taskId] = progress;
            
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                _logger.LogInformation("Task {TaskId} progress: {Activity} ({Percent}% complete)", 
                    taskId, progress.CurrentActivity, progress.PercentComplete);
            }
        }

        public async Task<TaskProgress> GetTaskProgressAsync(string taskId)
        {
            return _taskProgress.GetValueOrDefault(taskId) ?? new TaskProgress 
            { 
                TaskId = taskId,
                CurrentActivity = "Task queued",
                PercentComplete = 0
            };
        }

        public async Task<string> GenerateVoiceResponseAsync(VoiceTaskResult result)
        {
            if (!result.Success)
            {
                return result.VoiceFriendlyResponse ?? 
                       $"I encountered an issue: {result.Error}. Would you like me to try a different approach?";
            }

            var response = new List<string>();

            // Start with summary
            if (!string.IsNullOrEmpty(result.Summary))
            {
                response.Add(result.Summary);
            }
            else
            {
                response.Add("I've completed your request.");
            }

            // Mention files created
            if (result.FilesCreated.Any())
            {
                var fileCount = result.FilesCreated.Count;
                var fileTypes = GetFileTypes(result.FilesCreated);
                
                if (fileCount == 1)
                {
                    response.Add($"I created {result.FilesCreated.First()}.");
                }
                else
                {
                    response.Add($"I created {fileCount} files including {string.Join(", ", fileTypes)}.");
                }
            }

            // Mention files modified
            if (result.FilesModified.Any())
            {
                response.Add($"I also updated {result.FilesModified.Count} existing files.");
            }

            // Add key outcomes if any
            if (result.KeyOutcomes.Any())
            {
                var keyPoints = result.KeyOutcomes
                    .Where(kvp => kvp.Key.StartsWith("key_"))
                    .Select(kvp => kvp.Value)
                    .Take(3);
                
                if (keyPoints.Any())
                {
                    response.Add($"Key points: {string.Join(", ", keyPoints)}.");
                }
            }

            // Add duration if significant
            if (result.Duration.TotalMinutes > 1)
            {
                response.Add($"This took {result.Duration.TotalMinutes:F1} minutes to complete.");
            }

            // Close with a question
            response.Add("Is there anything specific about this implementation you'd like me to explain or modify?");

            return string.Join(" ", response);
        }

        private List<string> GetFileTypes(List<string> filePaths)
        {
            var extensions = filePaths
                .Select(f => System.IO.Path.GetExtension(f))
                .Where(ext => !string.IsNullOrEmpty(ext))
                .Distinct()
                .Select(ext => ext.TrimStart('.'))
                .ToList();

            var typeDescriptions = new Dictionary<string, string>
            {
                ["py"] = "Python files",
                ["js"] = "JavaScript files",
                ["ts"] = "TypeScript files",
                ["cs"] = "C# files",
                ["java"] = "Java files",
                ["go"] = "Go files",
                ["html"] = "HTML files",
                ["css"] = "CSS files",
                ["json"] = "configuration files",
                ["md"] = "documentation"
            };

            return extensions
                .Select(ext => typeDescriptions.GetValueOrDefault(ext, $"{ext} files"))
                .ToList();
        }
    }
}