using System;
using System.Collections.Generic;
using System.Text;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IPromptTemplateService
    {
        string GetIdeationPrompt(string productIdea);
        string GetProductDesignPrompt(string productIdea, BusinessAnalysis businessContext);
        string GetTechnicalDesignPrompt(string productIdea, ProductDesign productContext);
        string GetArchitecturePrompt(string productIdea, TechnicalDesign technicalContext);
        string GetImplementationPrompt(ConversationSession session);
        string GetEnhancementPrompt(ConversationSession session, string enhancement);
        string GetPhaseTransitionPrompt(ConversationPhase from, ConversationPhase to);
    }

    public class PromptTemplateService : IPromptTemplateService
    {
        public string GetIdeationPrompt(string productIdea)
        {
            return $@"Analyze this business idea: ""{productIdea}""

As an expert business analyst and startup advisor, provide a comprehensive analysis covering:

1. **Problem Statement**: What specific problem does this solve? Who feels this pain most acutely?

2. **Market Analysis**: 
   - Total addressable market (TAM)
   - Market growth rate and trends
   - Key market drivers

3. **Target Users**: 
   - Primary user segments
   - User demographics and behaviors
   - Why they would adopt this solution

4. **Value Propositions**: 
   - Unique advantages over existing solutions
   - Key differentiators
   - Cost/time/efficiency gains

5. **Competitive Landscape**:
   - Direct competitors
   - Indirect alternatives
   - Market positioning opportunity

6. **Risks & Challenges**:
   - Technical feasibility risks
   - Regulatory/compliance concerns
   - Market adoption barriers
   - Operational challenges

7. **Revenue Model**:
   - Pricing strategy options
   - Revenue streams
   - Unit economics potential

8. **Feasibility Score**: Rate 1-10 with detailed justification

Format as structured JSON for parsing.";
        }

        public string GetProductDesignPrompt(string productIdea, BusinessAnalysis businessContext)
        {
            return $@"Design a product for: ""{productIdea}""

Based on this validated business opportunity:
- Problem: {businessContext.ProblemStatement}
- Target Users: {string.Join(", ", businessContext.TargetUsers)}
- Value Props: {string.Join(", ", businessContext.ValuePropositions)}
- Market Size: {businessContext.MarketAnalysis}

As a product design expert, create:

1. **Product Vision**: 
   - Clear, inspiring vision statement
   - Key product principles
   - Success definition

2. **Core Features (MVP)**:
   - Essential features for launch
   - Feature prioritization rationale
   - User value for each feature

3. **Future Features**:
   - Phase 2 enhancements
   - Long-term product roadmap
   - Innovation opportunities

4. **User Personas**:
   - 2-3 detailed personas
   - Goals, frustrations, behaviors
   - Jobs to be done

5. **User Flows**:
   - Primary user journey
   - Key interaction points
   - Friction reduction opportunities

6. **Success Metrics**:
   - North star metric
   - Supporting KPIs
   - User satisfaction measures

Format as structured JSON with detailed descriptions.";
        }

        public string GetTechnicalDesignPrompt(string productIdea, ProductDesign productContext)
        {
            return $@"Create technical design for: ""{productIdea}""

Product requirements:
- Vision: {productContext.ProductVision}
- Core Features: {string.Join(", ", productContext.CoreFeatures)}
- User Flows: {productContext.UserFlows.Count} defined flows

As a senior software architect, design:

1. **Technology Stack**:
   - Programming languages with justification
   - Frameworks and libraries
   - Database technologies
   - Infrastructure platforms
   - Development tools

2. **API Design**:
   - RESTful endpoints for core features
   - Request/response schemas
   - Authentication approach
   - Rate limiting strategy
   - Versioning approach

3. **Data Models**:
   - Core entities and relationships
   - Database schema design
   - Data validation rules
   - Indexing strategy

4. **Security Architecture**:
   - Authentication & authorization
   - Data encryption approach
   - Security best practices
   - Compliance requirements

5. **Integration Requirements**:
   - Third-party services needed
   - API integrations
   - Payment processing
   - Communication services

6. **Performance Requirements**:
   - Response time targets
   - Throughput requirements
   - Concurrent user support
   - Data volume projections

Format as structured JSON with implementation details.";
        }

        public string GetArchitecturePrompt(string productIdea, TechnicalDesign technicalContext)
        {
            return $@"Recommend system architecture for: ""{productIdea}""

Technical requirements:
- Stack: {string.Join(", ", technicalContext.TechnologyStack)}
- APIs: {technicalContext.ApiDesign.Count} endpoints
- Integrations: {string.Join(", ", technicalContext.IntegrationPoints)}
- Security: {string.Join(", ", technicalContext.SecurityRequirements)}

As a cloud architecture expert, recommend:

1. **Architecture Pattern**:
   - Microservices vs Monolith vs Serverless
   - Justification for choice
   - Trade-offs analysis

2. **System Components**:
   - Service breakdown
   - Component responsibilities
   - Communication patterns
   - Data flow design

3. **Deployment Strategy**:
   - Container orchestration
   - CI/CD pipeline design
   - Blue-green deployment
   - Rollback procedures

4. **Scaling Strategy**:
   - Horizontal vs vertical scaling
   - Auto-scaling policies
   - Load balancing approach
   - Caching strategy

5. **Infrastructure Choices**:
   - Cloud provider recommendation
   - Specific services to use
   - Cost optimization approach
   - Multi-region considerations

6. **Non-Functional Requirements**:
   - High availability design
   - Disaster recovery plan
   - Monitoring and alerting
   - Performance optimization

Format as structured JSON with detailed recommendations.";
        }

        public string GetImplementationPrompt(ConversationSession session)
        {
            var prompt = new StringBuilder();
            
            prompt.AppendLine($"Implement the following system based on our detailed design:");
            prompt.AppendLine();
            prompt.AppendLine($"**Project**: {session.Title}");
            prompt.AppendLine($"**Description**: {session.Description}");
            prompt.AppendLine();

            if (session.BusinessContext != null)
            {
                prompt.AppendLine("**Business Requirements**:");
                prompt.AppendLine($"- Problem: {session.BusinessContext.ProblemStatement}");
                prompt.AppendLine($"- Target Users: {string.Join(", ", session.BusinessContext.TargetUsers)}");
                prompt.AppendLine($"- Revenue Model: {session.BusinessContext.RevenueModel}");
                prompt.AppendLine();
            }

            if (session.ProductContext != null)
            {
                prompt.AppendLine("**Product Specifications**:");
                prompt.AppendLine($"- Vision: {session.ProductContext.ProductVision}");
                prompt.AppendLine("- Core Features to Implement:");
                foreach (var feature in session.ProductContext.CoreFeatures)
                {
                    prompt.AppendLine($"  • {feature}");
                }
                prompt.AppendLine();
            }

            if (session.TechnicalContext != null)
            {
                prompt.AppendLine("**Technical Requirements**:");
                prompt.AppendLine($"- Technology Stack: {string.Join(", ", session.TechnicalContext.TechnologyStack)}");
                prompt.AppendLine("- API Endpoints to Implement:");
                foreach (var api in session.TechnicalContext.ApiDesign)
                {
                    prompt.AppendLine($"  • {api.Method} {api.Path} - {api.Description}");
                }
                prompt.AppendLine("- Data Models:");
                foreach (var model in session.TechnicalContext.DataModels)
                {
                    prompt.AppendLine($"  • {model.Name} - {model.Description}");
                }
                prompt.AppendLine();
            }

            if (session.ArchitectureContext != null)
            {
                prompt.AppendLine("**Architecture Guidelines**:");
                prompt.AppendLine($"- Pattern: {session.ArchitectureContext.Pattern}");
                prompt.AppendLine($"- Deployment: {session.ArchitectureContext.DeploymentStrategy}");
                prompt.AppendLine($"- Key Components: {string.Join(", ", session.ArchitectureContext.Components)}");
                prompt.AppendLine();
            }

            prompt.AppendLine("**Implementation Instructions**:");
            prompt.AppendLine("1. Create a well-structured project following the specified architecture");
            prompt.AppendLine("2. Implement all core features with clean, maintainable code");
            prompt.AppendLine("3. Include appropriate error handling and validation");
            prompt.AppendLine("4. Add helpful comments explaining complex logic");
            prompt.AppendLine("5. Create a README with setup and usage instructions");

            return prompt.ToString();
        }

        public string GetEnhancementPrompt(ConversationSession session, string enhancement)
        {
            var prompt = new StringBuilder();
            
            prompt.AppendLine($"Enhance the existing {session.Title} implementation:");
            prompt.AppendLine();
            prompt.AppendLine($"**Enhancement Request**: {enhancement}");
            prompt.AppendLine();
            prompt.AppendLine("**Existing System Context**:");
            
            if (session.ProductContext != null)
            {
                prompt.AppendLine($"- Current Features: {string.Join(", ", session.ProductContext.CoreFeatures)}");
            }
            
            if (session.TechnicalContext != null)
            {
                prompt.AppendLine($"- Tech Stack: {string.Join(", ", session.TechnicalContext.TechnologyStack)}");
            }
            
            prompt.AppendLine();
            prompt.AppendLine("Please implement the requested enhancement while:");
            prompt.AppendLine("- Maintaining consistency with existing code style");
            prompt.AppendLine("- Following the established architecture patterns");
            prompt.AppendLine("- Ensuring backward compatibility");
            prompt.AppendLine("- Adding appropriate tests if applicable");

            return prompt.ToString();
        }

        public string GetPhaseTransitionPrompt(ConversationPhase from, ConversationPhase to)
        {
            return (from, to) switch
            {
                (ConversationPhase.Ideation, ConversationPhase.ProductDesign) => 
                    "Great! Your business idea shows strong potential. Let's now design the product experience and define the core features that will deliver value to your users.",
                
                (ConversationPhase.ProductDesign, ConversationPhase.TechnicalDesign) =>
                    "Excellent product vision! Now let's translate these requirements into a technical design, selecting the right technologies and defining the system architecture.",
                
                (ConversationPhase.TechnicalDesign, ConversationPhase.Architecture) =>
                    "The technical design looks solid. Let's now define the system architecture to ensure scalability, reliability, and maintainability.",
                
                (ConversationPhase.Architecture, ConversationPhase.Implementation) =>
                    "Perfect! We have a complete design. I'll now connect you with a specialized worker to build your solution following all the specifications we've defined.",
                
                _ => "Moving to the next phase of development..."
            };
        }
    }
}