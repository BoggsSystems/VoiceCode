using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IMultiPhaseCoordinationService
    {
        Task<string> ExecutePlanAsync(VoiceTaskPlan plan, CancellationToken cancellationToken = default);
        Task<PhaseExecutionResult> ExecutePhaseAsync(TaskPhase phase, SharedContext context, string workerId);
        Task<bool> CoordinateHandoffAsync(WorkerHandoff handoff);
        Task<SharedContext> GetSharedContextAsync(string planId);
        Task UpdateSharedContextAsync(string planId, string phaseId, Dictionary<string, object> updates);
        Task<List<PhaseExecutionResult>> GetPhaseResultsAsync(string planId);
        Task PauseExecutionAsync(string planId);
        Task ResumeExecutionAsync(string planId);
    }

    public class MultiPhaseCoordinationService : IMultiPhaseCoordinationService
    {
        private readonly ILogger<MultiPhaseCoordinationService> _logger;
        private readonly IWorkerManagementService _workerManagement;
        private readonly IWorkerPoolService _workerPool;
        private readonly IVoiceTaskPlanningService _taskPlanning;
        private readonly Dictionary<string, PlanExecution> _activeExecutions = new();
        private readonly Dictionary<string, SharedContext> _sharedContexts = new();
        private readonly SemaphoreSlim _coordinationLock = new(1, 1);

        public MultiPhaseCoordinationService(
            ILogger<MultiPhaseCoordinationService> logger,
            IWorkerManagementService workerManagement,
            IWorkerPoolService workerPool,
            IVoiceTaskPlanningService taskPlanning)
        {
            _logger = logger;
            _workerManagement = workerManagement;
            _workerPool = workerPool;
            _taskPlanning = taskPlanning;
        }

        public async Task<string> ExecutePlanAsync(VoiceTaskPlan plan, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting execution of plan {PlanId} with {PhaseCount} phases", 
                plan.PlanId, plan.Phases.Count);

            // Initialize shared context
            var sharedContext = new SharedContext
            {
                PlanId = plan.PlanId,
                GlobalVariables = new Dictionary<string, object>
                {
                    ["projectName"] = ExtractProjectName(plan.OriginalPrompt),
                    ["primaryLanguage"] = plan.Metadata.PrimaryLanguage,
                    ["frameworks"] = plan.Metadata.Frameworks,
                    ["startTime"] = DateTime.UtcNow
                }
            };
            _sharedContexts[plan.PlanId] = sharedContext;

            // Initialize plan execution
            var execution = new PlanExecution
            {
                Plan = plan,
                StartTime = DateTime.UtcNow,
                Status = ExecutionStatus.Running
            };
            _activeExecutions[plan.PlanId] = execution;

            try
            {
                // Update plan status
                plan.Status = PlanStatus.Executing;

                // Determine execution order
                var executionOrder = await _taskPlanning.DeterminePhaseExecutionOrderAsync(plan);
                var phaseGroups = GroupPhasesForExecution(executionOrder, plan.Strategy);

                // Execute phase groups
                foreach (var group in phaseGroups)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await HandleCancellationAsync(plan.PlanId);
                        break;
                    }

                    // Check if we need user confirmation
                    if (plan.Strategy.RequireConfirmationBetweenPhases && execution.CompletedPhases.Any())
                    {
                        execution.WaitingForConfirmation = true;
                        _logger.LogInformation("Waiting for user confirmation before phase group: {Phases}", 
                            string.Join(", ", group.Select(p => p.Name)));
                        
                        // In a real implementation, this would wait for user input
                        // For now, we'll simulate automatic confirmation after a delay
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                        execution.WaitingForConfirmation = false;
                    }

                    // Execute phases in parallel if allowed
                    if (group.Count > 1 && plan.Strategy.Mode != ExecutionMode.Sequential)
                    {
                        await ExecutePhaseGroupParallelAsync(group, sharedContext, execution, cancellationToken);
                    }
                    else
                    {
                        foreach (var phase in group)
                        {
                            await ExecuteSinglePhaseAsync(phase, sharedContext, execution, cancellationToken);
                        }
                    }

                    // Update progress
                    await _taskPlanning.CalculateProgressAsync(plan.PlanId);
                }

                // Finalize execution
                execution.EndTime = DateTime.UtcNow;
                execution.Status = execution.FailedPhases.Any() ? ExecutionStatus.CompletedWithErrors : ExecutionStatus.Completed;
                plan.Status = execution.FailedPhases.Any() ? PlanStatus.Failed : PlanStatus.Completed;

                _logger.LogInformation("Plan {PlanId} execution completed. Status: {Status}", 
                    plan.PlanId, execution.Status);

                return plan.PlanId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing plan {PlanId}", plan.PlanId);
                execution.Status = ExecutionStatus.Failed;
                plan.Status = PlanStatus.Failed;
                throw;
            }
        }

        private List<List<TaskPhase>> GroupPhasesForExecution(List<TaskPhase> phases, ExecutionStrategy strategy)
        {
            var groups = new List<List<TaskPhase>>();
            var processed = new HashSet<string>();

            foreach (var phase in phases)
            {
                if (processed.Contains(phase.PhaseId)) continue;

                var group = new List<TaskPhase> { phase };
                processed.Add(phase.PhaseId);

                // Find other phases that can run in parallel
                if (strategy.Mode != ExecutionMode.Sequential)
                {
                    var parallel = phases
                        .Where(p => !processed.Contains(p.PhaseId) &&
                                   p.Dependencies.All(d => processed.Contains(d) || 
                                                          group.Any(g => g.PhaseId == d)))
                        .Take(strategy.MaxParallelPhases - 1)
                        .ToList();

                    group.AddRange(parallel);
                    foreach (var p in parallel)
                    {
                        processed.Add(p.PhaseId);
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        private async Task ExecutePhaseGroupParallelAsync(
            List<TaskPhase> phases, 
            SharedContext context, 
            PlanExecution execution,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Executing {Count} phases in parallel", phases.Count);

            var tasks = phases.Select(phase => ExecuteSinglePhaseAsync(phase, context, execution, cancellationToken));
            await Task.WhenAll(tasks);
        }

        private async Task ExecuteSinglePhaseAsync(
            TaskPhase phase, 
            SharedContext context, 
            PlanExecution execution,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting execution of phase: {PhaseName}", phase.Name);
                
                phase.Status = PhaseStatus.InProgress;
                phase.StartedAt = DateTime.UtcNow;

                // Select appropriate worker based on specialization
                var worker = await SelectWorkerForPhaseAsync(phase);
                if (worker == null)
                {
                    throw new InvalidOperationException($"No suitable worker found for phase {phase.Name}");
                }

                // Execute phase on worker
                var result = await ExecutePhaseAsync(phase, context, worker.WorkerId);

                if (result.Success)
                {
                    phase.Status = PhaseStatus.Completed;
                    phase.CompletedAt = DateTime.UtcNow;
                    phase.Artifacts = result.Artifacts;
                    execution.CompletedPhases.Add(phase.PhaseId);

                    // Update shared context with phase results
                    await UpdateSharedContextAsync(execution.Plan.PlanId, phase.PhaseId, result.ContextUpdates);

                    _logger.LogInformation("Phase {PhaseName} completed successfully", phase.Name);
                }
                else
                {
                    await HandlePhaseFailureAsync(phase, result, execution);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing phase {PhaseName}", phase.Name);
                await HandlePhaseFailureAsync(phase, new PhaseExecutionResult 
                { 
                    Success = false, 
                    Error = ex.Message 
                }, execution);
            }
        }

        private async Task<WorkerRegistration> SelectWorkerForPhaseAsync(TaskPhase phase)
        {
            // Create a pseudo-task to leverage worker pool selection
            var task = new ClaudeCodeTask
            {
                Id = Guid.NewGuid().ToString(),
                Type = "phase_execution",
                Prompt = phase.Description,
                Context = new Dictionary<string, string>
                {
                    ["specialization"] = phase.RequiredSpecialization.ToString(),
                    ["phaseType"] = phase.Type.ToString(),
                    ["estimatedMinutes"] = phase.EstimatedMinutes.ToString()
                }
            };

            // Use capability-based selection for specialized workers
            return await _workerPool.ApplyLoadBalancingStrategyAsync(task, LoadBalancingStrategy.CapabilityBased);
        }

        public async Task<PhaseExecutionResult> ExecutePhaseAsync(TaskPhase phase, SharedContext context, string workerId)
        {
            var result = new PhaseExecutionResult
            {
                PhaseId = phase.PhaseId,
                WorkerId = workerId,
                StartTime = DateTime.UtcNow
            };

            try
            {
                // Convert phase to worker tasks
                var workerTasks = ConvertPhaseToWorkerTasks(phase, context);
                
                foreach (var workerTask in workerTasks)
                {
                    var taskId = await _workerManagement.SubmitTaskAsync(workerTask);
                    var taskResult = await _workerManagement.GetTaskResultAsync(taskId, phase.Type == PhaseType.Testing 
                        ? TimeSpan.FromMinutes(30) // More time for testing
                        : TimeSpan.FromMinutes(20));

                    if (taskResult == null || !taskResult.Success)
                    {
                        result.Success = false;
                        result.Error = taskResult?.Error ?? "Task timeout";
                        break;
                    }

                    // Collect artifacts
                    foreach (var file in taskResult.FileOperations)
                    {
                        result.Artifacts.Add(new PhaseArtifact
                        {
                            Type = "file",
                            Path = file.FilePath,
                            Description = file.Description ?? $"Generated by {phase.Name}"
                        });
                    }

                    // Update context
                    if (taskResult.Metadata != null)
                    {
                        foreach (var kvp in taskResult.Metadata)
                        {
                            result.ContextUpdates[kvp.Key] = kvp.Value;
                        }
                    }
                }

                result.Success = true;
                result.EndTime = DateTime.UtcNow;
                result.Summary = $"Phase {phase.Name} completed successfully with {result.Artifacts.Count} artifacts";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.EndTime = DateTime.UtcNow;
                _logger.LogError(ex, "Error executing phase {PhaseId} on worker {WorkerId}", phase.PhaseId, workerId);
            }

            return result;
        }

        private List<WorkerTask> ConvertPhaseToWorkerTasks(TaskPhase phase, SharedContext context)
        {
            var tasks = new List<WorkerTask>();

            // If phase already has tasks defined, use them
            if (phase.Tasks.Any())
            {
                return phase.Tasks;
            }

            // Otherwise, create tasks based on phase type
            var baseContext = new Dictionary<string, string>
            {
                ["phaseId"] = phase.PhaseId,
                ["phaseName"] = phase.Name,
                ["projectName"] = context.GlobalVariables.GetValueOrDefault("projectName")?.ToString() ?? "project"
            };

            // Add shared context
            foreach (var kvp in context.GlobalVariables)
            {
                baseContext[$"global_{kvp.Key}"] = kvp.Value?.ToString() ?? "";
            }

            var task = new WorkerTask
            {
                Type = DetermineTaskType(phase.Type),
                Description = phase.Description,
                Input = GeneratePhasePrompt(phase, context),
                Context = baseContext,
                WorkspaceId = context.PlanId
            };

            tasks.Add(task);
            return tasks;
        }

        private string DetermineTaskType(PhaseType phaseType)
        {
            return phaseType switch
            {
                PhaseType.Setup => "project_setup",
                PhaseType.Design => "design",
                PhaseType.Implementation => "code_generation",
                PhaseType.Testing => "testing",
                PhaseType.Documentation => "documentation",
                PhaseType.Deployment => "deployment",
                PhaseType.Optimization => "optimization",
                _ => "general"
            };
        }

        private string GeneratePhasePrompt(TaskPhase phase, SharedContext context)
        {
            var prompt = phase.Description + "\n\n";

            // Add context about previous phases
            if (context.SharedFiles.Any())
            {
                prompt += "Previous phases have created the following files:\n";
                foreach (var file in context.SharedFiles.Values.Take(10)) // Limit to avoid huge prompts
                {
                    prompt += $"- {file.FilePath}: {file.Description}\n";
                }
                prompt += "\n";
            }

            // Add specific instructions based on phase type
            prompt += phase.Type switch
            {
                PhaseType.Setup => "Create the initial project structure with all necessary configuration files.",
                PhaseType.Implementation => "Implement the functionality described above. Follow best practices and include error handling.",
                PhaseType.Testing => "Create comprehensive tests for the implemented functionality. Include unit tests and integration tests where appropriate.",
                PhaseType.Documentation => "Generate clear documentation including README, API docs, and inline comments.",
                _ => "Complete the task as described."
            };

            return prompt;
        }

        private async Task HandlePhaseFailureAsync(TaskPhase phase, PhaseExecutionResult result, PlanExecution execution)
        {
            phase.Status = PhaseStatus.Failed;
            phase.CompletedAt = DateTime.UtcNow;
            execution.FailedPhases.Add(phase.PhaseId);

            _logger.LogError("Phase {PhaseName} failed: {Error}", phase.Name, result.Error);

            // Check retry policy
            var strategy = execution.Plan.Strategy;
            if (strategy.RetryPolicy.RetryablePhases.Contains(phase.Type) && 
                execution.PhaseRetries.GetValueOrDefault(phase.PhaseId, 0) < strategy.RetryPolicy.MaxRetries)
            {
                _logger.LogInformation("Scheduling retry for phase {PhaseName}", phase.Name);
                
                // Increment retry count
                execution.PhaseRetries[phase.PhaseId] = execution.PhaseRetries.GetValueOrDefault(phase.PhaseId, 0) + 1;
                
                // Wait before retry
                await Task.Delay(strategy.RetryPolicy.RetryDelay);
                
                // Reset phase status for retry
                phase.Status = PhaseStatus.Queued;
                execution.FailedPhases.Remove(phase.PhaseId);
            }
        }

        public async Task<bool> CoordinateHandoffAsync(WorkerHandoff handoff)
        {
            try
            {
                _logger.LogInformation("Coordinating handoff from {FromWorker} to {ToWorker} for phase {PhaseId}", 
                    handoff.FromWorkerId, handoff.ToWorkerId, handoff.PhaseId);

                // Release task from current worker
                await _workerPool.ReleaseTaskFromWorkerAsync(handoff.PhaseId, handoff.FromWorkerId);

                // Assign to new worker
                await _workerPool.AssignTaskToWorkerAsync(handoff.PhaseId, handoff.ToWorkerId);

                // Update metrics for both workers
                await _workerPool.UpdateWorkerMetricsAsync(handoff.FromWorkerId, new TaskResult
                {
                    TaskId = handoff.PhaseId,
                    Success = true,
                    Duration = DateTime.UtcNow - handoff.HandoffTime,
                    TaskType = "handoff"
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error coordinating handoff");
                return false;
            }
        }

        public async Task<SharedContext> GetSharedContextAsync(string planId)
        {
            await _coordinationLock.WaitAsync();
            try
            {
                return _sharedContexts.GetValueOrDefault(planId) ?? new SharedContext { PlanId = planId };
            }
            finally
            {
                _coordinationLock.Release();
            }
        }

        public async Task UpdateSharedContextAsync(string planId, string phaseId, Dictionary<string, object> updates)
        {
            await _coordinationLock.WaitAsync();
            try
            {
                if (!_sharedContexts.TryGetValue(planId, out var context))
                {
                    context = new SharedContext { PlanId = planId };
                    _sharedContexts[planId] = context;
                }

                // Apply updates
                foreach (var update in updates)
                {
                    context.GlobalVariables[update.Key] = update.Value;
                }

                // Record update history
                context.UpdateHistory.Add(new ContextUpdate
                {
                    Timestamp = DateTime.UtcNow,
                    PhaseId = phaseId,
                    UpdateType = "phase_completion",
                    Changes = updates
                });

                context.LastUpdated = DateTime.UtcNow;
            }
            finally
            {
                _coordinationLock.Release();
            }
        }

        public async Task<List<PhaseExecutionResult>> GetPhaseResultsAsync(string planId)
        {
            if (_activeExecutions.TryGetValue(planId, out var execution))
            {
                return execution.PhaseResults;
            }
            return new List<PhaseExecutionResult>();
        }

        public async Task PauseExecutionAsync(string planId)
        {
            if (_activeExecutions.TryGetValue(planId, out var execution))
            {
                execution.Status = ExecutionStatus.Paused;
                execution.Plan.Status = PlanStatus.Paused;
                _logger.LogInformation("Paused execution of plan {PlanId}", planId);
            }
        }

        public async Task ResumeExecutionAsync(string planId)
        {
            if (_activeExecutions.TryGetValue(planId, out var execution))
            {
                execution.Status = ExecutionStatus.Running;
                execution.Plan.Status = PlanStatus.Executing;
                _logger.LogInformation("Resumed execution of plan {PlanId}", planId);
            }
        }

        private async Task HandleCancellationAsync(string planId)
        {
            if (_activeExecutions.TryGetValue(planId, out var execution))
            {
                execution.Status = ExecutionStatus.Cancelled;
                execution.Plan.Status = PlanStatus.Cancelled;
                _logger.LogInformation("Cancelled execution of plan {PlanId}", planId);
            }
        }

        private string ExtractProjectName(string prompt)
        {
            // Simple extraction - in production, use NLP
            var words = prompt.Split(' ')
                             .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 3)
                             .Take(3);
            return string.Join("", words);
        }

        private class PlanExecution
        {
            public VoiceTaskPlan Plan { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public ExecutionStatus Status { get; set; }
            public List<string> CompletedPhases { get; set; } = new();
            public List<string> FailedPhases { get; set; } = new();
            public Dictionary<string, int> PhaseRetries { get; set; } = new();
            public List<PhaseExecutionResult> PhaseResults { get; set; } = new();
            public bool WaitingForConfirmation { get; set; }
        }

        private enum ExecutionStatus
        {
            Running,
            Paused,
            Completed,
            CompletedWithErrors,
            Failed,
            Cancelled
        }
    }

    public class PhaseExecutionResult
    {
        public string PhaseId { get; set; }
        public string WorkerId { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<PhaseArtifact> Artifacts { get; set; } = new();
        public Dictionary<string, object> ContextUpdates { get; set; } = new();
        public string Summary { get; set; }
    }
}