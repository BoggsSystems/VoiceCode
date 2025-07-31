using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IChatGPTService
    {
        Task<BusinessAnalysis> AnalyzeBusinessOpportunityAsync(string productIdea);
        Task<ProductDesign> GenerateProductDesignAsync(string productIdea, BusinessAnalysis businessContext);
        Task<TechnicalDesign> GenerateTechnicalDesignAsync(string productIdea, ProductDesign productContext);
        Task<ArchitectureDesign> RecommendArchitectureAsync(string productIdea, TechnicalDesign technicalContext);
        Task<string> GeneratePhaseResponseAsync(ConversationPhase phase, string userInput, ConversationSession session);
        Task<string> SummarizeForVoiceAsync(object analysisData, ConversationPhase phase);
    }

    public class ChatGPTService : IChatGPTService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChatGPTService> _logger;
        private readonly string _apiKey;
        private readonly string _model;

        public ChatGPTService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ChatGPTService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _apiKey = configuration["OpenAI:ApiKey"];
            _model = configuration["OpenAI:Model"] ?? "gpt-4";

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", _apiKey);
                _logger.LogInformation("OpenAI API key configured successfully");
            }
            else
            {
                _logger.LogError("OpenAI API key not found in configuration. Please ensure the OpenAI__ApiKey environment variable is set from Key Vault.");
                throw new InvalidOperationException("OpenAI API key is required but not configured");
            }
        }

        public async Task<BusinessAnalysis> AnalyzeBusinessOpportunityAsync(string productIdea)
        {
            var prompt = $@"Analyze this business idea: ""{productIdea}""

Please provide a comprehensive business analysis including:
1. Problem Statement - What problem does this solve?
2. Market Analysis - Size, growth, trends
3. Target Users - Who would use this?
4. Value Propositions - Why is this better than alternatives?
5. Competitors - Who else is doing this?
6. Risks - Technical, regulatory, market risks
7. Revenue Model - How would this make money?
8. Feasibility Score - Rate 1-10 with explanation

Format the response as JSON with these exact fields:
{{
  ""problemStatement"": ""..."",
  ""marketAnalysis"": ""..."",
  ""targetUsers"": [""...""],
  ""valuePropositions"": [""...""],
  ""competitors"": [""...""],
  ""risks"": [""...""],
  ""revenueModel"": ""..."",
  ""feasibilityScore"": ""..."",
  ""additionalInsights"": {{}}
}}";

            var response = await SendChatCompletionAsync(prompt, temperature: 0.7);
            return ParseJsonResponse<BusinessAnalysis>(response);
        }

        public async Task<ProductDesign> GenerateProductDesignAsync(string productIdea, BusinessAnalysis businessContext)
        {
            var prompt = $@"Design a product for: ""{productIdea}""

Based on this business context:
- Problem: {businessContext.ProblemStatement}
- Target Users: {string.Join(", ", businessContext.TargetUsers)}
- Value Props: {string.Join(", ", businessContext.ValuePropositions)}

Please provide:
1. Product Vision - Clear statement of what we're building
2. Core Features - MVP features needed
3. Future Features - Nice-to-have features
4. User Personas - Detailed personas with goals and pain points
5. User Flows - Key user journeys
6. Success Metrics - How we measure success

Format as JSON:
{{
  ""productVision"": ""..."",
  ""coreFeatures"": [""...""],
  ""futureFeatures"": [""...""],
  ""userPersonas"": [
    {{
      ""name"": ""..."",
      ""description"": ""..."",
      ""goals"": [""...""],
      ""painPoints"": [""...""]
    }}
  ],
  ""userFlows"": [
    {{
      ""name"": ""..."",
      ""description"": ""..."",
      ""steps"": [""...""]
    }}
  ],
  ""successMetrics"": ""...""
}}";

            var response = await SendChatCompletionAsync(prompt, temperature: 0.7);
            return ParseJsonResponse<ProductDesign>(response);
        }

        public async Task<TechnicalDesign> GenerateTechnicalDesignAsync(string productIdea, ProductDesign productContext)
        {
            var prompt = $@"Create technical design for: ""{productIdea}""

Product details:
- Vision: {productContext.ProductVision}
- Core Features: {string.Join(", ", productContext.CoreFeatures)}

Please provide:
1. Technology Stack - Languages, frameworks, databases
2. API Design - Key endpoints with methods and schemas
3. Data Models - Core entities and relationships
4. Security Requirements - Auth, encryption, compliance
5. Integration Points - External services needed
6. Performance Requirements - Latency, throughput, scale

Format as JSON:
{{
  ""technologyStack"": [""...""],
  ""apiDesign"": [
    {{
      ""method"": ""..."",
      ""path"": ""..."",
      ""description"": ""..."",
      ""requestSchema"": {{}},
      ""responseSchema"": {{}}
    }}
  ],
  ""dataModels"": [
    {{
      ""name"": ""..."",
      ""description"": ""..."",
      ""fields"": {{}},
      ""relationships"": [""...""]
    }}
  ],
  ""securityRequirements"": [""...""],
  ""integrationPoints"": [""...""],
  ""performanceRequirements"": {{}}
}}";

            var response = await SendChatCompletionAsync(prompt, temperature: 0.7);
            return ParseJsonResponse<TechnicalDesign>(response);
        }

        public async Task<ArchitectureDesign> RecommendArchitectureAsync(string productIdea, TechnicalDesign technicalContext)
        {
            var prompt = $@"Recommend architecture for: ""{productIdea}""

Technical requirements:
- Stack: {string.Join(", ", technicalContext.TechnologyStack)}
- Integrations: {string.Join(", ", technicalContext.IntegrationPoints)}
- Security: {string.Join(", ", technicalContext.SecurityRequirements)}

Please recommend:
1. Architecture Pattern - Microservices, serverless, monolith, etc.
2. Components - Major system components
3. Deployment Strategy - How to deploy
4. Scaling Strategy - How to scale
5. Infrastructure Choices - Cloud services, databases, etc.
6. Non-functional Requirements - Reliability, monitoring, etc.

Format as JSON:
{{
  ""pattern"": ""..."",
  ""components"": [""...""],
  ""deploymentStrategy"": ""..."",
  ""scalingStrategy"": ""..."",
  ""infrastructureChoices"": {{}},
  ""nonFunctionalRequirements"": [""...""]
}}";

            var response = await SendChatCompletionAsync(prompt, temperature: 0.7);
            return ParseJsonResponse<ArchitectureDesign>(response);
        }

        public async Task<string> GeneratePhaseResponseAsync(
            ConversationPhase phase, 
            string userInput, 
            ConversationSession session)
        {
            var contextPrompt = BuildContextPrompt(session);
            var phasePrompt = GetPhaseSpecificPrompt(phase, userInput);
            
            var fullPrompt = $@"{contextPrompt}

Current phase: {phase}
User input: {userInput}

{phasePrompt}";

            return await SendChatCompletionAsync(fullPrompt, temperature: 0.8);
        }

        private async Task<string> SendChatCompletionAsync(string prompt, double temperature = 0.7)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are an expert software architect and business analyst helping to design and build software solutions." },
                    new { role = "user", content = prompt }
                },
                temperature = temperature,
                max_tokens = 2000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);
                
                return responseData
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling ChatGPT API");
                throw;
            }
        }

        private T ParseJsonResponse<T>(string response) where T : new()
        {
            try
            {
                // Extract JSON from the response (in case it's wrapped in markdown or text)
                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}') + 1;
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart);
                    return JsonSerializer.Deserialize<T>(jsonString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JSON response");
            }

            return new T();
        }

        private string BuildContextPrompt(ConversationSession session)
        {
            var context = new StringBuilder();
            
            if (session.BusinessContext != null)
            {
                context.AppendLine("Business Context:");
                context.AppendLine($"- Problem: {session.BusinessContext.ProblemStatement}");
                context.AppendLine($"- Market: {session.BusinessContext.MarketAnalysis}");
            }

            if (session.ProductContext != null)
            {
                context.AppendLine("\nProduct Context:");
                context.AppendLine($"- Vision: {session.ProductContext.ProductVision}");
                context.AppendLine($"- Features: {string.Join(", ", session.ProductContext.CoreFeatures)}");
            }

            if (session.TechnicalContext != null)
            {
                context.AppendLine("\nTechnical Context:");
                context.AppendLine($"- Stack: {string.Join(", ", session.TechnicalContext.TechnologyStack)}");
            }

            return context.ToString();
        }

        private string GetPhaseSpecificPrompt(ConversationPhase phase, string userInput)
        {
            return phase switch
            {
                ConversationPhase.Ideation => 
                    "Help the user explore and refine their idea. Ask clarifying questions if needed.",
                
                ConversationPhase.ProductDesign => 
                    "Help design the product features and user experience.",
                
                ConversationPhase.TechnicalDesign => 
                    "Help design the technical implementation details.",
                
                ConversationPhase.Architecture => 
                    "Recommend the best architecture for this solution.",
                
                ConversationPhase.Implementation => 
                    "The user is ready to build. Prepare clear implementation instructions.",
                
                _ => "Continue the conversation naturally."
            };
        }

        public async Task<string> SummarizeForVoiceAsync(object analysisData, ConversationPhase phase)
        {
            string contextPrompt = phase switch
            {
                ConversationPhase.Ideation => "a business opportunity analysis",
                ConversationPhase.ProductDesign => "a product design specification",
                ConversationPhase.TechnicalDesign => "a technical design document",
                ConversationPhase.Architecture => "an architecture recommendation",
                _ => "an analysis"
            };

            var prompt = $@"Summarize the following {contextPrompt} for voice output in 2-3 sentences maximum.

The summary should:
- Be natural and conversational for text-to-speech
- Highlight the most important insights
- Be encouraging and constructive
- End with a brief suggestion for next steps
- Avoid technical jargon
- Use simple sentence structures

Analysis data:
{JsonSerializer.Serialize(analysisData, new JsonSerializerOptions { WriteIndented = true })}

Voice summary:";

            try
            {
                var response = await SendChatCompletionAsync(prompt, temperature: 0.8);
                _logger.LogDebug("Generated voice summary for {Phase}: {Summary}", phase, response);
                return response.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate voice summary");
                // Fallback summary
                return phase switch
                {
                    ConversationPhase.Ideation => "I've completed the business analysis. The opportunity looks promising with some challenges to consider.",
                    ConversationPhase.ProductDesign => "I've created a product design with core features and user flows defined.",
                    ConversationPhase.TechnicalDesign => "The technical design is ready with technology stack and API specifications.",
                    ConversationPhase.Architecture => "I've prepared architecture recommendations for scalability and reliability.",
                    _ => "The analysis is complete. Let me know if you'd like to explore any details."
                };
            }
        }
    }
}