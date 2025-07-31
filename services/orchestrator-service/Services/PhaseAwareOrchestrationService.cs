using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IPhaseAwareOrchestrationService
    {
        Task<OrchestrationResult> ProcessVoiceCommandAsync(string userId, string voiceCommand);
        Task<List<ConversationSession>> GetUserSessionsAsync(string userId);
        Task<ConversationSession> GetSessionStatusAsync(string sessionId);
    }

    public class PhaseAwareOrchestrationService : IPhaseAwareOrchestrationService
    {
        private readonly ILogger<PhaseAwareOrchestrationService> _logger;
        private readonly ISessionStorageService _sessionStorage;
        private readonly IChatGPTService _chatGPTService;
        private readonly IWorkerManagementService _workerService;
        private readonly ITTSServiceClient _ttsService;
        private readonly List<PhaseTransition> _phaseTransitions;

        public PhaseAwareOrchestrationService(
            ILogger<PhaseAwareOrchestrationService> logger,
            ISessionStorageService sessionStorage,
            IChatGPTService chatGPTService,
            IWorkerManagementService workerService,
            ITTSServiceClient ttsService)
        {
            _logger = logger;
            _sessionStorage = sessionStorage;
            _chatGPTService = chatGPTService;
            _workerService = workerService;
            _ttsService = ttsService;
            _phaseTransitions = InitializePhaseTransitions();
        }

        public async Task<OrchestrationResult> ProcessVoiceCommandAsync(string userId, string voiceCommand)
        {
            _logger.LogInformation("Processing voice command for user {UserId}: {Command}", userId, voiceCommand);

            // Step 1: Determine if this relates to an existing session or is new
            var session = await DetermineSessionAsync(userId, voiceCommand);
            
            if (session == null)
            {
                // New idea - start ideation phase
                session = await _sessionStorage.CreateSessionAsync(userId, voiceCommand);
                _logger.LogInformation("Created new session {SessionId} for ideation", session.SessionId);
            }

            // Step 2: Process based on current phase
            var result = await ProcessPhaseAsync(session, voiceCommand);

            // Step 3: Check for phase transitions
            var newPhase = DeterminePhaseTransition(session, voiceCommand);
            if (newPhase != session.CurrentPhase)
            {
                _logger.LogInformation("Transitioning from {OldPhase} to {NewPhase}", 
                    session.CurrentPhase, newPhase);
                session.CurrentPhase = newPhase;
                
                // Additional processing for phase transition
                result = await HandlePhaseTransitionAsync(session, newPhase, voiceCommand);
            }

            // Step 4: Save conversation turn
            await _sessionStorage.AddConversationTurnAsync(session.SessionId, new ConversationTurn
            {
                Phase = session.CurrentPhase,
                UserInput = voiceCommand,
                SystemResponse = result.Message,
                Metadata = result.Metadata
            });

            // Step 5: Update session
            await _sessionStorage.UpdateSessionAsync(session);

            return result;
        }

        private async Task<ConversationSession> DetermineSessionAsync(string userId, string voiceCommand)
        {
            var lowerCommand = voiceCommand.ToLower();

            // Check for explicit session references
            if (ContainsSessionReference(lowerCommand))
            {
                return await _sessionStorage.FindSessionByContextAsync(userId, voiceCommand);
            }

            // Check for continuation keywords
            if (ContainsContinuationKeywords(lowerCommand))
            {
                return await _sessionStorage.GetActiveSessionForUserAsync(userId);
            }

            // Check for new idea indicators
            if (ContainsNewIdeaIndicators(lowerCommand))
            {
                return null; // Force new session
            }

            // Try to find by context
            return await _sessionStorage.FindSessionByContextAsync(userId, voiceCommand);
        }

        private async Task<OrchestrationResult> ProcessPhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            switch (session.CurrentPhase)
            {
                case ConversationPhase.Ideation:
                    return await ProcessIdeationPhaseAsync(session, voiceCommand);
                
                case ConversationPhase.ProductDesign:
                    return await ProcessProductDesignPhaseAsync(session, voiceCommand);
                
                case ConversationPhase.TechnicalDesign:
                    return await ProcessTechnicalDesignPhaseAsync(session, voiceCommand);
                
                case ConversationPhase.Architecture:
                    return await ProcessArchitecturePhaseAsync(session, voiceCommand);
                
                case ConversationPhase.Implementation:
                    return await ProcessImplementationPhaseAsync(session, voiceCommand);
                
                case ConversationPhase.Enhancement:
                    return await ProcessEnhancementPhaseAsync(session, voiceCommand);
                
                default:
                    throw new InvalidOperationException($"Unknown phase: {session.CurrentPhase}");
            }
        }

        private async Task<OrchestrationResult> ProcessIdeationPhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            // Analyze business opportunity
            var businessAnalysis = await _chatGPTService.AnalyzeBusinessOpportunityAsync(voiceCommand);
            session.BusinessContext = businessAnalysis;

            // Generate response
            var response = $@"I've analyzed your idea for: {session.Title}

**Business Analysis:**
- **Problem**: {businessAnalysis.ProblemStatement}
- **Market**: {businessAnalysis.MarketAnalysis}
- **Target Users**: {string.Join(", ", businessAnalysis.TargetUsers)}
- **Feasibility**: {businessAnalysis.FeasibilityScore}

**Key Risks**: {string.Join(", ", businessAnalysis.Risks.Take(3))}

This looks {(businessAnalysis.FeasibilityScore.Contains("7") || businessAnalysis.FeasibilityScore.Contains("8") || businessAnalysis.FeasibilityScore.Contains("9") ? "promising" : "challenging")}. 

Would you like to proceed with product design, or would you like to explore a different approach?";

            // Generate voice summary
            var (voiceSummary, audioUrl) = await GenerateVoiceResponseAsync(businessAnalysis, session.CurrentPhase);

            return new OrchestrationResult
            {
                Success = true,
                Message = response,
                VoiceSummary = voiceSummary,
                AudioUrl = audioUrl,
                SessionId = session.SessionId,
                Phase = session.CurrentPhase.ToString(),
                Metadata = new Dictionary<string, object>
                {
                    ["businessAnalysis"] = businessAnalysis,
                    ["suggestedNextSteps"] = new[] { "product design", "refine idea", "explore alternatives" }
                }
            };
        }

        private async Task<OrchestrationResult> ProcessProductDesignPhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            var productDesign = await _chatGPTService.GenerateProductDesignAsync(
                session.Description, 
                session.BusinessContext);
            
            session.ProductContext = productDesign;

            var response = $@"Here's the product design for your {session.Title}:

**Product Vision**: {productDesign.ProductVision}

**Core Features (MVP)**:
{string.Join("\n", productDesign.CoreFeatures.Select(f => $"- {f}"))}

**Key User Flow**: {productDesign.UserFlows.FirstOrDefault()?.Name}
{string.Join("\n", productDesign.UserFlows.FirstOrDefault()?.Steps.Select(s => $"  → {s}") ?? new List<string>())}

Ready to move to technical design, or would you like to refine any aspects?";

            // Generate voice summary
            var (voiceSummary, audioUrl) = await GenerateVoiceResponseAsync(productDesign, session.CurrentPhase);

            return new OrchestrationResult
            {
                Success = true,
                Message = response,
                VoiceSummary = voiceSummary,
                AudioUrl = audioUrl,
                SessionId = session.SessionId,
                Phase = session.CurrentPhase.ToString()
            };
        }

        private async Task<OrchestrationResult> ProcessTechnicalDesignPhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            var technicalDesign = await _chatGPTService.GenerateTechnicalDesignAsync(
                session.Description,
                session.ProductContext);
            
            session.TechnicalContext = technicalDesign;

            var response = $@"Technical design for {session.Title}:

**Technology Stack**:
{string.Join("\n", technicalDesign.TechnologyStack.Select(t => $"- {t}"))}

**Key APIs**:
{string.Join("\n", technicalDesign.ApiDesign.Take(3).Select(api => $"- {api.Method} {api.Path}: {api.Description}"))}

**Core Data Models**:
{string.Join("\n", technicalDesign.DataModels.Select(m => $"- {m.Name}: {m.Description}"))}

Shall we proceed with architecture recommendations?";

            // Generate voice summary
            var (voiceSummary, audioUrl) = await GenerateVoiceResponseAsync(technicalDesign, session.CurrentPhase);

            return new OrchestrationResult
            {
                Success = true,
                Message = response,
                VoiceSummary = voiceSummary,
                AudioUrl = audioUrl,
                SessionId = session.SessionId,
                Phase = session.CurrentPhase.ToString()
            };
        }

        private async Task<OrchestrationResult> ProcessArchitecturePhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            var architecture = await _chatGPTService.RecommendArchitectureAsync(
                session.Description,
                session.TechnicalContext);
            
            session.ArchitectureContext = architecture;

            var response = $@"Architecture recommendation for {session.Title}:

**Pattern**: {architecture.Pattern}
**Deployment**: {architecture.DeploymentStrategy}
**Scaling**: {architecture.ScalingStrategy}

**Key Components**:
{string.Join("\n", architecture.Components.Select(c => $"- {c}"))}

We're ready to start building. Shall I connect you with a worker to begin implementation?";

            // Generate voice summary
            var (voiceSummary, audioUrl) = await GenerateVoiceResponseAsync(architecture, session.CurrentPhase);

            return new OrchestrationResult
            {
                Success = true,
                Message = response,
                VoiceSummary = voiceSummary,
                AudioUrl = audioUrl,
                SessionId = session.SessionId,
                Phase = session.CurrentPhase.ToString(),
                Metadata = new Dictionary<string, object>
                {
                    ["readyForImplementation"] = true
                }
            };
        }

        private async Task<OrchestrationResult> ProcessImplementationPhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            // Check if worker is already assigned
            if (string.IsNullOrEmpty(session.AssignedWorkerId))
            {
                // Create implementation context
                var implementationContext = BuildImplementationContext(session);
                
                // Submit to worker
                var workerTask = new WorkerTask
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = WorkerTaskType.Implementation,
                    Description = $"Implement {session.Title}",
                    WorkspaceId = session.WorkspaceId,
                    Parameters = new Dictionary<string, object>
                    {
                        ["voiceCommand"] = $"Create {session.Description} with the following specifications: {implementationContext}",
                        ["sessionId"] = session.SessionId,
                        ["implementationContext"] = implementationContext
                    }
                };

                var workerResult = await _workerService.SubmitTaskAsync(workerTask);
                
                if (workerResult.Success)
                {
                    session.AssignedWorkerId = workerResult.AssignedWorkerId;
                    session.CurrentTaskId = workerTask.Id;
                    session.WorkerStatus = WorkerTaskStatus.Queued;
                }

                return new OrchestrationResult
                {
                    Success = workerResult.Success,
                    Message = $"I've started implementing your {session.Title}. Worker {workerResult.AssignedWorkerId} is now building the solution based on all our design decisions.",
                    SessionId = session.SessionId,
                    Phase = session.CurrentPhase.ToString(),
                    WorkerId = workerResult.AssignedWorkerId
                };
            }
            else
            {
                // Enhancement to existing implementation
                var enhancementTask = new WorkerTask
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = WorkerTaskType.Enhancement,
                    Description = voiceCommand,
                    WorkspaceId = session.WorkspaceId,
                    Parameters = new Dictionary<string, object>
                    {
                        ["voiceCommand"] = voiceCommand,
                        ["sessionId"] = session.SessionId,
                        ["previousContext"] = BuildImplementationContext(session)
                    }
                };

                var workerResult = await _workerService.SubmitTaskToWorkerAsync(
                    session.AssignedWorkerId, 
                    enhancementTask);

                return new OrchestrationResult
                {
                    Success = workerResult.Success,
                    Message = $"I've sent your request to continue work on {session.Title}.",
                    SessionId = session.SessionId,
                    Phase = session.CurrentPhase.ToString(),
                    WorkerId = session.AssignedWorkerId
                };
            }
        }

        private async Task<OrchestrationResult> ProcessEnhancementPhaseAsync(
            ConversationSession session, 
            string voiceCommand)
        {
            // Similar to implementation but for enhancements
            return await ProcessImplementationPhaseAsync(session, voiceCommand);
        }

        private ConversationPhase DeterminePhaseTransition(
            ConversationSession session, 
            string voiceCommand)
        {
            var lowerCommand = voiceCommand.ToLower();
            
            foreach (var transition in _phaseTransitions)
            {
                if (transition.FromPhase == session.CurrentPhase &&
                    transition.TriggerPhrases.Any(phrase => lowerCommand.Contains(phrase)))
                {
                    if (transition.ValidationRule == null || transition.ValidationRule(session))
                    {
                        return transition.ToPhase;
                    }
                }
            }

            return session.CurrentPhase;
        }

        private async Task<OrchestrationResult> HandlePhaseTransitionAsync(
            ConversationSession session,
            ConversationPhase newPhase,
            string voiceCommand)
        {
            // Special handling for transitions
            if (newPhase == ConversationPhase.Implementation && 
                session.CurrentPhase != ConversationPhase.Implementation)
            {
                return await ProcessImplementationPhaseAsync(session, voiceCommand);
            }

            return await ProcessPhaseAsync(session, voiceCommand);
        }

        private string BuildImplementationContext(ConversationSession session)
        {
            var context = new System.Text.StringBuilder();
            
            context.AppendLine($"Project: {session.Title}");
            context.AppendLine($"Description: {session.Description}");
            
            if (session.BusinessContext != null)
            {
                context.AppendLine($"\nBusiness Context:");
                context.AppendLine($"- Problem: {session.BusinessContext.ProblemStatement}");
                context.AppendLine($"- Users: {string.Join(", ", session.BusinessContext.TargetUsers)}");
            }

            if (session.ProductContext != null)
            {
                context.AppendLine($"\nProduct Requirements:");
                context.AppendLine($"- Vision: {session.ProductContext.ProductVision}");
                context.AppendLine($"- Features: {string.Join(", ", session.ProductContext.CoreFeatures)}");
            }

            if (session.TechnicalContext != null)
            {
                context.AppendLine($"\nTechnical Specifications:");
                context.AppendLine($"- Stack: {string.Join(", ", session.TechnicalContext.TechnologyStack)}");
                context.AppendLine($"- APIs to implement:");
                foreach (var api in session.TechnicalContext.ApiDesign)
                {
                    context.AppendLine($"  - {api.Method} {api.Path}: {api.Description}");
                }
            }

            if (session.ArchitectureContext != null)
            {
                context.AppendLine($"\nArchitecture:");
                context.AppendLine($"- Pattern: {session.ArchitectureContext.Pattern}");
                context.AppendLine($"- Components: {string.Join(", ", session.ArchitectureContext.Components)}");
            }

            return context.ToString();
        }

        private List<PhaseTransition> InitializePhaseTransitions()
        {
            return new List<PhaseTransition>
            {
                // Ideation to Product Design
                new PhaseTransition
                {
                    FromPhase = ConversationPhase.Ideation,
                    ToPhase = ConversationPhase.ProductDesign,
                    TriggerPhrases = new List<string> 
                    { 
                        "proceed", "let's design", "sounds good", "yes", "continue",
                        "product design", "move forward"
                    },
                    ValidationRule = s => s.BusinessContext != null
                },
                
                // Product Design to Technical Design
                new PhaseTransition
                {
                    FromPhase = ConversationPhase.ProductDesign,
                    ToPhase = ConversationPhase.TechnicalDesign,
                    TriggerPhrases = new List<string> 
                    { 
                        "technical design", "ready", "continue", "yes", "proceed",
                        "let's get technical", "move to technical"
                    },
                    ValidationRule = s => s.ProductContext != null
                },
                
                // Technical Design to Architecture
                new PhaseTransition
                {
                    FromPhase = ConversationPhase.TechnicalDesign,
                    ToPhase = ConversationPhase.Architecture,
                    TriggerPhrases = new List<string> 
                    { 
                        "architecture", "yes", "proceed", "continue", "ready",
                        "recommend architecture", "let's architect"
                    },
                    ValidationRule = s => s.TechnicalContext != null
                },
                
                // Architecture to Implementation
                new PhaseTransition
                {
                    FromPhase = ConversationPhase.Architecture,
                    ToPhase = ConversationPhase.Implementation,
                    TriggerPhrases = new List<string> 
                    { 
                        "build", "implement", "let's code", "start building", "yes",
                        "create it", "make it", "develop", "begin implementation"
                    },
                    ValidationRule = s => s.ArchitectureContext != null
                },
                
                // Any phase to Implementation (skip ahead)
                new PhaseTransition
                {
                    FromPhase = ConversationPhase.Ideation,
                    ToPhase = ConversationPhase.Implementation,
                    TriggerPhrases = new List<string> 
                    { 
                        "just build it", "skip to implementation", "create it now"
                    }
                },
            };
        }

        private bool ContainsSessionReference(string command)
        {
            var sessionKeywords = new[] 
            { 
                "forex", "restaurant", "api", "platform", "app", "application",
                "system", "service", "project"
            };
            
            return sessionKeywords.Any(keyword => command.Contains(keyword));
        }

        private bool ContainsContinuationKeywords(string command)
        {
            var continuationKeywords = new[] 
            { 
                "continue", "status", "check on", "how's", "update on",
                "keep working", "resume", "go back to"
            };
            
            return continuationKeywords.Any(keyword => command.Contains(keyword));
        }

        private bool ContainsNewIdeaIndicators(string command)
        {
            var newIdeaKeywords = new[] 
            { 
                "new idea", "i'm thinking", "what if", "how about",
                "i want to create", "let's build something new"
            };
            
            return newIdeaKeywords.Any(keyword => command.Contains(keyword));
        }

        private async Task<(string voiceSummary, string audioUrl)> GenerateVoiceResponseAsync(object analysisData, ConversationPhase phase)
        {
            try
            {
                var voiceSummary = await _chatGPTService.SummarizeForVoiceAsync(analysisData, phase);
                var audioUrl = await _ttsService.GenerateAudioAsync(voiceSummary);
                return (voiceSummary, audioUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate voice response");
                return (string.Empty, string.Empty);
            }
        }

        public async Task<List<ConversationSession>> GetUserSessionsAsync(string userId)
        {
            return await _sessionStorage.GetUserSessionsAsync(userId);
        }

        public async Task<ConversationSession> GetSessionStatusAsync(string sessionId)
        {
            var session = await _sessionStorage.GetSessionAsync(sessionId);
            
            if (session != null && !string.IsNullOrEmpty(session.AssignedWorkerId))
            {
                // Get latest worker status
                var workerStatus = await _workerService.GetWorkerStatusAsync(session.AssignedWorkerId);
                if (workerStatus != null)
                {
                    session.WorkerStatus = workerStatus.State == WorkerState.Busy 
                        ? WorkerTaskStatus.InProgress 
                        : WorkerTaskStatus.Completed;
                }
            }

            return session;
        }
    }

    public class OrchestrationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string VoiceSummary { get; set; }
        public string AudioUrl { get; set; }
        public string SessionId { get; set; }
        public string Phase { get; set; }
        public string WorkerId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}