using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IVoiceTaskPlanningService
    {
        Task<VoiceTaskPlan> CreatePlanFromVoicePromptAsync(string voicePrompt, string sessionId);
        Task<VoiceTaskPlan> OptimizeForVoiceExecutionAsync(VoiceTaskPlan plan);
        Task<List<TaskPhase>> DeterminePhaseExecutionOrderAsync(VoiceTaskPlan plan);
        Task<Dictionary<string, WorkerSpecialization>> AssignSpecializationsAsync(VoiceTaskPlan plan);
        Task<VoiceProgress> CalculateProgressAsync(string planId);
        Task<string> GenerateVoiceProgressReportAsync(string planId);
    }

    public class VoiceTaskPlanningService : IVoiceTaskPlanningService
    {
        private readonly ILogger<VoiceTaskPlanningService> _logger;
        private readonly Dictionary<string, VoiceTaskPlan> _activePlans = new();
        
        // Keywords that indicate different types of projects
        private readonly Dictionary<string, string[]> _projectTypeKeywords = new()
        {
            ["web-app"] = new[] { "web app", "website", "web application", "frontend", "ui", "user interface" },
            ["api"] = new[] { "api", "rest", "graphql", "backend", "service", "endpoint" },
            ["cli-tool"] = new[] { "cli", "command line", "terminal", "console" },
            ["mobile-app"] = new[] { "mobile", "ios", "android", "app" },
            ["microservice"] = new[] { "microservice", "distributed", "service" },
            ["full-stack"] = new[] { "full stack", "fullstack", "complete application" }
        };

        // Phase templates based on project type
        private readonly Dictionary<string, List<PhaseTemplate>> _phaseTemplates = new();

        public VoiceTaskPlanningService(ILogger<VoiceTaskPlanningService> logger)
        {
            _logger = logger;
            InitializePhaseTemplates();
        }

        private void InitializePhaseTemplates()
        {
            // Web App phases
            _phaseTemplates["web-app"] = new List<PhaseTemplate>
            {
                new() { Type = PhaseType.Setup, Name = "Project Setup", EstimatedMinutes = 5, Specialization = WorkerSpecialization.General },
                new() { Type = PhaseType.Design, Name = "UI/UX Design", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Frontend },
                new() { Type = PhaseType.Implementation, Name = "Frontend Implementation", EstimatedMinutes = 15, Specialization = WorkerSpecialization.Frontend },
                new() { Type = PhaseType.Implementation, Name = "Backend Integration", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Backend },
                new() { Type = PhaseType.Testing, Name = "Testing & Validation", EstimatedMinutes = 5, Specialization = WorkerSpecialization.Testing },
                new() { Type = PhaseType.Optimization, Name = "Performance Optimization", EstimatedMinutes = 5, Specialization = WorkerSpecialization.Performance }
            };

            // API phases
            _phaseTemplates["api"] = new List<PhaseTemplate>
            {
                new() { Type = PhaseType.Setup, Name = "API Project Setup", EstimatedMinutes = 5, Specialization = WorkerSpecialization.General },
                new() { Type = PhaseType.Design, Name = "API Design & Schema", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Backend },
                new() { Type = PhaseType.Implementation, Name = "Endpoint Implementation", EstimatedMinutes = 15, Specialization = WorkerSpecialization.Backend },
                new() { Type = PhaseType.Implementation, Name = "Database Layer", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Database },
                new() { Type = PhaseType.Testing, Name = "API Testing", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Testing },
                new() { Type = PhaseType.Documentation, Name = "API Documentation", EstimatedMinutes = 5, Specialization = WorkerSpecialization.Documentation }
            };

            // Full-stack phases
            _phaseTemplates["full-stack"] = new List<PhaseTemplate>
            {
                new() { Type = PhaseType.Setup, Name = "Full-Stack Setup", EstimatedMinutes = 5, Specialization = WorkerSpecialization.General },
                new() { Type = PhaseType.Design, Name = "Architecture Design", EstimatedMinutes = 10, Specialization = WorkerSpecialization.General },
                new() { Type = PhaseType.Implementation, Name = "Database Schema", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Database },
                new() { Type = PhaseType.Implementation, Name = "Backend Services", EstimatedMinutes = 15, Specialization = WorkerSpecialization.Backend },
                new() { Type = PhaseType.Implementation, Name = "Frontend Development", EstimatedMinutes = 15, Specialization = WorkerSpecialization.Frontend },
                new() { Type = PhaseType.Testing, Name = "Integration Testing", EstimatedMinutes = 10, Specialization = WorkerSpecialization.Testing },
                new() { Type = PhaseType.Deployment, Name = "Deployment Setup", EstimatedMinutes = 5, Specialization = WorkerSpecialization.DevOps }
            };
        }

        public async Task<VoiceTaskPlan> CreatePlanFromVoicePromptAsync(string voicePrompt, string sessionId)
        {
            _logger.LogInformation("Creating task plan from voice prompt: {Prompt}", voicePrompt);

            var plan = new VoiceTaskPlan
            {
                SessionId = sessionId,
                OriginalPrompt = voicePrompt,
                Metadata = await AnalyzePromptAsync(voicePrompt)
            };

            // Determine project type
            var projectType = DetermineProjectType(voicePrompt);
            plan.Metadata.ProjectType = projectType;

            // Estimate complexity
            plan.Metadata.Complexity = EstimateComplexity(voicePrompt);

            // Create phases based on project type
            plan.Phases = await CreatePhasesAsync(projectType, voicePrompt, plan.Metadata);

            // Set execution strategy
            plan.Strategy = DetermineExecutionStrategy(plan);

            // Optimize for voice execution
            plan = await OptimizeForVoiceExecutionAsync(plan);

            // Store the plan
            _activePlans[plan.PlanId] = plan;

            _logger.LogInformation("Created task plan {PlanId} with {PhaseCount} phases", 
                plan.PlanId, plan.Phases.Count);

            return plan;
        }

        private async Task<PlanMetadata> AnalyzePromptAsync(string prompt)
        {
            var metadata = new PlanMetadata();

            // Extract technologies
            var techKeywords = new Dictionary<string, string[]>
            {
                ["csharp"] = new[] { "c#", "csharp", ".net", "dotnet", "asp.net" },
                ["python"] = new[] { "python", "django", "flask", "fastapi" },
                ["javascript"] = new[] { "javascript", "js", "node", "nodejs", "react", "angular", "vue" },
                ["typescript"] = new[] { "typescript", "ts" },
                ["java"] = new[] { "java", "spring", "springboot" },
                ["go"] = new[] { "go", "golang" }
            };

            var lowerPrompt = prompt.ToLower();
            
            foreach (var tech in techKeywords)
            {
                if (tech.Value.Any(keyword => lowerPrompt.Contains(keyword)))
                {
                    metadata.Technologies.Add(tech.Key);
                    if (string.IsNullOrEmpty(metadata.PrimaryLanguage))
                    {
                        metadata.PrimaryLanguage = tech.Key;
                    }
                }
            }

            // Extract frameworks
            var frameworkKeywords = new[]
            {
                "react", "angular", "vue", "express", "fastapi", "django", "flask",
                "spring", "asp.net", "blazor", "next.js", "nuxt", "svelte"
            };

            metadata.Frameworks = frameworkKeywords
                .Where(fw => lowerPrompt.Contains(fw))
                .ToList();

            // Estimate lines of code based on keywords
            metadata.EstimatedLinesOfCode = EstimateLinesOfCode(prompt);

            return metadata;
        }

        private string DetermineProjectType(string prompt)
        {
            var lowerPrompt = prompt.ToLower();
            
            foreach (var type in _projectTypeKeywords)
            {
                if (type.Value.Any(keyword => lowerPrompt.Contains(keyword)))
                {
                    return type.Key;
                }
            }

            // Default to API if no specific type detected
            return "api";
        }

        private ComplexityLevel EstimateComplexity(string prompt)
        {
            var complexityIndicators = new Dictionary<ComplexityLevel, string[]>
            {
                [ComplexityLevel.VeryComplex] = new[] { "enterprise", "large scale", "distributed", "microservices", "complex" },
                [ComplexityLevel.Complex] = new[] { "full stack", "complete", "comprehensive", "advanced" },
                [ComplexityLevel.Moderate] = new[] { "standard", "typical", "normal", "basic crud" },
                [ComplexityLevel.Simple] = new[] { "simple", "basic", "hello world", "example", "demo" }
            };

            var lowerPrompt = prompt.ToLower();

            foreach (var indicator in complexityIndicators.OrderByDescending(i => i.Key))
            {
                if (indicator.Value.Any(keyword => lowerPrompt.Contains(keyword)))
                {
                    return indicator.Key;
                }
            }

            return ComplexityLevel.Moderate;
        }

        private int EstimateLinesOfCode(string prompt)
        {
            var complexity = EstimateComplexity(prompt);
            
            return complexity switch
            {
                ComplexityLevel.Simple => 100,
                ComplexityLevel.Moderate => 500,
                ComplexityLevel.Complex => 2000,
                ComplexityLevel.VeryComplex => 5000,
                _ => 500
            };
        }

        private async Task<List<TaskPhase>> CreatePhasesAsync(string projectType, string prompt, PlanMetadata metadata)
        {
            var phases = new List<TaskPhase>();
            
            if (!_phaseTemplates.ContainsKey(projectType))
            {
                projectType = "api"; // fallback
            }

            var templates = _phaseTemplates[projectType];
            var phaseNumber = 1;

            foreach (var template in templates)
            {
                var phase = new TaskPhase
                {
                    PhaseNumber = phaseNumber++,
                    Name = template.Name,
                    Type = template.Type,
                    EstimatedMinutes = AdjustTimeForComplexity(template.EstimatedMinutes, metadata.Complexity),
                    RequiredSpecialization = template.Specialization,
                    Description = GeneratePhaseDescription(template, prompt, metadata)
                };

                // Add dependencies
                if (phase.Type != PhaseType.Setup && phases.Any())
                {
                    // Most phases depend on setup
                    var setupPhase = phases.FirstOrDefault(p => p.Type == PhaseType.Setup);
                    if (setupPhase != null)
                    {
                        phase.Dependencies.Add(setupPhase.PhaseId);
                    }

                    // Testing depends on implementation
                    if (phase.Type == PhaseType.Testing)
                    {
                        var implPhases = phases.Where(p => p.Type == PhaseType.Implementation).ToList();
                        phase.Dependencies.AddRange(implPhases.Select(p => p.PhaseId));
                    }
                }

                phases.Add(phase);
            }

            return phases;
        }

        private int AdjustTimeForComplexity(int baseMinutes, ComplexityLevel complexity)
        {
            var multiplier = complexity switch
            {
                ComplexityLevel.Simple => 0.5,
                ComplexityLevel.Moderate => 1.0,
                ComplexityLevel.Complex => 1.5,
                ComplexityLevel.VeryComplex => 2.0,
                _ => 1.0
            };

            return (int)(baseMinutes * multiplier);
        }

        private string GeneratePhaseDescription(PhaseTemplate template, string prompt, PlanMetadata metadata)
        {
            return template.Type switch
            {
                PhaseType.Setup => $"Initialize {metadata.ProjectType} project with {metadata.PrimaryLanguage} and required dependencies",
                PhaseType.Design => $"Design {template.Name} based on requirements: {prompt.Substring(0, Math.Min(100, prompt.Length))}...",
                PhaseType.Implementation => $"Implement {template.Name} using {string.Join(", ", metadata.Technologies)}",
                PhaseType.Testing => $"Create and run tests for {metadata.ProjectType} functionality",
                PhaseType.Documentation => $"Generate documentation for {metadata.ProjectType}",
                PhaseType.Deployment => $"Configure deployment for {metadata.ProjectType}",
                PhaseType.Optimization => $"Optimize performance and code quality",
                _ => template.Name
            };
        }

        private ExecutionStrategy DetermineExecutionStrategy(VoiceTaskPlan plan)
        {
            var strategy = new ExecutionStrategy();

            // For complex projects, allow more parallel execution
            if (plan.Metadata.Complexity >= ComplexityLevel.Complex)
            {
                strategy.Mode = ExecutionMode.Adaptive;
                strategy.MaxParallelPhases = 3;
            }
            else
            {
                strategy.Mode = ExecutionMode.Sequential;
                strategy.MaxParallelPhases = 1;
            }

            // For voice interaction, always require confirmation between major phases
            strategy.RequireConfirmationBetweenPhases = true;
            
            // 20-minute phases for optimal voice interaction
            strategy.PhaseTimeout = TimeSpan.FromMinutes(20);

            return strategy;
        }

        public async Task<VoiceTaskPlan> OptimizeForVoiceExecutionAsync(VoiceTaskPlan plan)
        {
            _logger.LogInformation("Optimizing plan {PlanId} for voice execution", plan.PlanId);

            // Group small phases to avoid too many interruptions
            var optimizedPhases = new List<TaskPhase>();
            var currentGroup = new List<TaskPhase>();
            var currentGroupTime = 0;

            foreach (var phase in plan.Phases)
            {
                currentGroup.Add(phase);
                currentGroupTime += phase.EstimatedMinutes;

                // If group reaches ~15 minutes or is a natural boundary, finalize it
                if (currentGroupTime >= 15 || phase.Type == PhaseType.Testing || phase.Type == PhaseType.Deployment)
                {
                    if (currentGroup.Count > 1)
                    {
                        // Merge phases
                        var mergedPhase = MergePhases(currentGroup);
                        optimizedPhases.Add(mergedPhase);
                    }
                    else
                    {
                        optimizedPhases.AddRange(currentGroup);
                    }

                    currentGroup.Clear();
                    currentGroupTime = 0;
                }
            }

            // Add any remaining phases
            if (currentGroup.Any())
            {
                if (currentGroup.Count > 1)
                {
                    optimizedPhases.Add(MergePhases(currentGroup));
                }
                else
                {
                    optimizedPhases.AddRange(currentGroup);
                }
            }

            plan.Phases = optimizedPhases;

            // Renumber phases
            for (int i = 0; i < plan.Phases.Count; i++)
            {
                plan.Phases[i].PhaseNumber = i + 1;
            }

            return plan;
        }

        private TaskPhase MergePhases(List<TaskPhase> phases)
        {
            var mergedPhase = new TaskPhase
            {
                PhaseNumber = phases.First().PhaseNumber,
                Name = $"Combined: {string.Join(" + ", phases.Select(p => p.Name))}",
                Type = phases.First().Type, // Use the primary type
                EstimatedMinutes = phases.Sum(p => p.EstimatedMinutes),
                RequiredSpecialization = DetermineSpecializationForMerged(phases),
                Description = $"Combined phase executing: {string.Join(", ", phases.Select(p => p.Name))}",
                Dependencies = phases.SelectMany(p => p.Dependencies).Distinct().ToList()
            };

            // Merge all tasks from constituent phases
            foreach (var phase in phases)
            {
                mergedPhase.Tasks.AddRange(phase.Tasks);
            }

            return mergedPhase;
        }

        private WorkerSpecialization DetermineSpecializationForMerged(List<TaskPhase> phases)
        {
            // If all phases have the same specialization, use it
            var specializations = phases.Select(p => p.RequiredSpecialization).Distinct().ToList();
            
            if (specializations.Count == 1)
            {
                return specializations.First();
            }

            // Otherwise, pick the most specialized
            var priority = new[] 
            { 
                WorkerSpecialization.Frontend, 
                WorkerSpecialization.Backend, 
                WorkerSpecialization.Database,
                WorkerSpecialization.Testing,
                WorkerSpecialization.General 
            };

            return priority.FirstOrDefault(p => specializations.Contains(p));
        }

        public async Task<List<TaskPhase>> DeterminePhaseExecutionOrderAsync(VoiceTaskPlan plan)
        {
            var executionOrder = new List<TaskPhase>();
            var completed = new HashSet<string>();
            var remaining = plan.Phases.ToList();

            while (remaining.Any())
            {
                // Find phases with no dependencies or all dependencies completed
                var ready = remaining.Where(p => 
                    !p.Dependencies.Any() || 
                    p.Dependencies.All(d => completed.Contains(d))
                ).ToList();

                if (!ready.Any())
                {
                    _logger.LogWarning("Circular dependency detected in plan {PlanId}", plan.PlanId);
                    break;
                }

                // Sort by phase number to maintain logical order
                ready = ready.OrderBy(p => p.PhaseNumber).ToList();

                // Based on execution strategy, determine how many can run in parallel
                var toExecute = plan.Strategy.Mode switch
                {
                    ExecutionMode.Sequential => ready.Take(1).ToList(),
                    ExecutionMode.Parallel => ready.Take(plan.Strategy.MaxParallelPhases).ToList(),
                    ExecutionMode.Adaptive => DetermineAdaptiveExecution(ready, plan.Strategy.MaxParallelPhases),
                    _ => ready.Take(1).ToList()
                };

                executionOrder.AddRange(toExecute);
                foreach (var phase in toExecute)
                {
                    completed.Add(phase.PhaseId);
                    remaining.Remove(phase);
                }
            }

            return executionOrder;
        }

        private List<TaskPhase> DetermineAdaptiveExecution(List<TaskPhase> ready, int maxParallel)
        {
            // Group by specialization to run similar tasks together
            var groups = ready.GroupBy(p => p.RequiredSpecialization)
                              .OrderByDescending(g => g.Count())
                              .ToList();

            var toExecute = new List<TaskPhase>();
            
            foreach (var group in groups)
            {
                if (toExecute.Count >= maxParallel) break;
                
                var available = maxParallel - toExecute.Count;
                toExecute.AddRange(group.Take(available));
            }

            return toExecute;
        }

        public async Task<Dictionary<string, WorkerSpecialization>> AssignSpecializationsAsync(VoiceTaskPlan plan)
        {
            var assignments = new Dictionary<string, WorkerSpecialization>();

            foreach (var phase in plan.Phases)
            {
                assignments[phase.PhaseId] = phase.RequiredSpecialization;
            }

            return assignments;
        }

        public async Task<VoiceProgress> CalculateProgressAsync(string planId)
        {
            if (!_activePlans.TryGetValue(planId, out var plan))
            {
                return new VoiceProgress();
            }

            var progress = new VoiceProgress
            {
                TotalPhases = plan.Phases.Count,
                CompletedPhases = plan.Phases.Count(p => p.Status == PhaseStatus.Completed),
                FailedPhases = plan.Phases.Count(p => p.Status == PhaseStatus.Failed)
            };

            var currentPhase = plan.Phases.FirstOrDefault(p => p.Status == PhaseStatus.InProgress);
            if (currentPhase != null)
            {
                progress.CurrentPhase = currentPhase.Name;
            }

            // Calculate elapsed time
            if (plan.Phases.Any(p => p.StartedAt.HasValue))
            {
                var firstStart = plan.Phases.Where(p => p.StartedAt.HasValue)
                                           .Min(p => p.StartedAt.Value);
                progress.ElapsedTime = DateTime.UtcNow - firstStart;
            }

            // Estimate remaining time
            var remainingMinutes = plan.Phases
                .Where(p => p.Status == PhaseStatus.Pending || p.Status == PhaseStatus.InProgress)
                .Sum(p => p.EstimatedMinutes);
            progress.EstimatedTimeRemaining = TimeSpan.FromMinutes(remainingMinutes);

            // Key achievements
            progress.KeyAchievements = plan.Phases
                .Where(p => p.Status == PhaseStatus.Completed)
                .Select(p => $"Completed {p.Name}")
                .ToList();

            // Next milestone
            var nextPhase = plan.Phases.FirstOrDefault(p => p.Status == PhaseStatus.Pending);
            if (nextPhase != null)
            {
                progress.NextMilestone = nextPhase.Name;
            }

            plan.Progress = progress;
            return progress;
        }

        public async Task<string> GenerateVoiceProgressReportAsync(string planId)
        {
            var progress = await CalculateProgressAsync(planId);
            
            if (!_activePlans.TryGetValue(planId, out var plan))
            {
                return "No active plan found.";
            }

            var report = new List<string>();
            
            // Overall progress
            report.Add(progress.GetVoiceSummary());

            // Recent achievements
            if (progress.KeyAchievements.Any())
            {
                report.Add($"Recent achievements: {string.Join(", ", progress.KeyAchievements.TakeLast(3))}.");
            }

            // Current focus
            if (!string.IsNullOrEmpty(progress.CurrentPhase))
            {
                var currentPhase = plan.Phases.FirstOrDefault(p => p.Name == progress.CurrentPhase);
                if (currentPhase != null && currentPhase.Artifacts.Any())
                {
                    report.Add($"Created {currentPhase.Artifacts.Count} artifacts in current phase.");
                }
            }

            // Next steps
            if (!string.IsNullOrEmpty(progress.NextMilestone))
            {
                report.Add($"Next up: {progress.NextMilestone}.");
            }

            // Time estimate
            if (progress.EstimatedTimeRemaining.TotalMinutes > 0)
            {
                report.Add($"Estimated time to completion: {progress.EstimatedTimeRemaining.TotalMinutes:F0} minutes.");
            }

            return string.Join(" ", report);
        }

        private class PhaseTemplate
        {
            public PhaseType Type { get; set; }
            public string Name { get; set; }
            public int EstimatedMinutes { get; set; }
            public WorkerSpecialization Specialization { get; set; }
        }
    }
}