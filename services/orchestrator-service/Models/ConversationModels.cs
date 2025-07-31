using System;
using System.Collections.Generic;

namespace VoiceCode.OrchestratorService.Models
{
    public enum ConversationPhase
    {
        Ideation,
        ProductDesign,
        TechnicalDesign,
        Architecture,
        Implementation,
        Enhancement
    }

    public class ConversationSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }
        public string WorkspaceId { get; set; }
        public ConversationPhase CurrentPhase { get; set; } = ConversationPhase.Ideation;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
        public string Title { get; set; }
        public string Description { get; set; }
        
        // Context from each phase
        public BusinessAnalysis BusinessContext { get; set; }
        public ProductDesign ProductContext { get; set; }
        public TechnicalDesign TechnicalContext { get; set; }
        public ArchitectureDesign ArchitectureContext { get; set; }
        
        // Conversation history
        public List<ConversationTurn> History { get; set; } = new();
        
        // Worker association (when in implementation phase)
        public string AssignedWorkerId { get; set; }
        public string CurrentTaskId { get; set; }
        public WorkerTaskStatus WorkerStatus { get; set; }
    }

    public class ConversationTurn
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public ConversationPhase Phase { get; set; }
        public string UserInput { get; set; }
        public string SystemResponse { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class BusinessAnalysis
    {
        public string ProblemStatement { get; set; }
        public string MarketAnalysis { get; set; }
        public List<string> TargetUsers { get; set; } = new();
        public List<string> ValuePropositions { get; set; } = new();
        public List<string> Competitors { get; set; } = new();
        public List<string> Risks { get; set; } = new();
        public string RevenueModel { get; set; }
        public string FeasibilityScore { get; set; }
        public Dictionary<string, object> AdditionalInsights { get; set; } = new();
    }

    public class ProductDesign
    {
        public string ProductVision { get; set; }
        public List<string> CoreFeatures { get; set; } = new();
        public List<string> FutureFeatures { get; set; } = new();
        public List<UserPersona> UserPersonas { get; set; } = new();
        public List<UserFlow> UserFlows { get; set; } = new();
        public string SuccessMetrics { get; set; }
    }

    public class UserPersona
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Goals { get; set; } = new();
        public List<string> PainPoints { get; set; } = new();
    }

    public class UserFlow
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Steps { get; set; } = new();
    }

    public class TechnicalDesign
    {
        public List<string> TechnologyStack { get; set; } = new();
        public List<ApiEndpoint> ApiDesign { get; set; } = new();
        public List<DataModel> DataModels { get; set; } = new();
        public List<string> SecurityRequirements { get; set; } = new();
        public List<string> IntegrationPoints { get; set; } = new();
        public Dictionary<string, object> PerformanceRequirements { get; set; } = new();
    }

    public class ApiEndpoint
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> RequestSchema { get; set; }
        public Dictionary<string, object> ResponseSchema { get; set; }
    }

    public class DataModel
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new();
        public List<string> Relationships { get; set; } = new();
    }

    public class ArchitectureDesign
    {
        public string Pattern { get; set; } // Microservices, Monolith, Serverless, etc.
        public List<string> Components { get; set; } = new();
        public string DeploymentStrategy { get; set; }
        public string ScalingStrategy { get; set; }
        public Dictionary<string, string> InfrastructureChoices { get; set; } = new();
        public List<string> NonFunctionalRequirements { get; set; } = new();
    }

    public class PhaseTransition
    {
        public ConversationPhase FromPhase { get; set; }
        public ConversationPhase ToPhase { get; set; }
        public List<string> TriggerPhrases { get; set; } = new();
        public Func<ConversationSession, bool> ValidationRule { get; set; }
    }
}