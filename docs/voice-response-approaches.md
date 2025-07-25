# Voice Response Generation Approaches

## Current Approach: Post-Processing with OpenAI

```
User Voice → Claude (technical response) → OpenAI Interpreter → Voice-friendly response
```

**Pros:**
- Claude focuses on accurate code generation
- Separation of concerns
- Can switch interpreters easily

**Cons:**
- Extra API call (cost + latency)
- Potential information loss in translation
- Two AI models that could disagree

## Proposed Approach: Direct Voice-Friendly Generation

```
User Voice → Claude (with voice-aware prompts) → Voice-friendly response
```

**Pros:**
- Single API call (faster, cheaper)
- No information loss
- Claude maintains full context
- More coherent responses

**Cons:**
- Might impact code generation quality
- Need to carefully balance instructions
- Harder to A/B test different voice styles

## Hybrid Approach: Structured Voice Responses

We could modify Claude to return **structured responses** that include both code and voice summaries:

```json
{
  "voice_summary": {
    "brief": "I've created a login form with email validation",
    "actions": ["Created LoginForm.tsx", "Added email validation", "Styled with Material-UI"],
    "next_steps": "Would you like me to add password strength checking?",
    "warnings": ["Remember to install Material-UI dependencies"]
  },
  "technical_details": {
    "code": "// ... full code here ...",
    "explanation": "// ... technical explanation ..."
  }
}
```

## Implementation Strategy

### 1. Modify System Prompts for Voice Context

```csharp
// In PromptTemplateService.cs
private const string VOICE_RESPONSE_INSTRUCTIONS = @"
When responding for voice output:

1. Start with a brief 1-2 sentence summary of what you did
2. List key actions taken (maximum 3-5 bullet points)
3. Mention any important warnings or prerequisites
4. Suggest logical next steps
5. Keep technical jargon to a minimum
6. Use conversational language

Example voice response structure:
- Brief: 'I've created a React login component with email validation'
- Actions: ['Created LoginForm component', 'Added email validation', 'Integrated with your auth system']
- Warning: 'You'll need to install the validation library'
- Next: 'Would you like me to add password requirements?'
";
```

### 2. Create Voice-Aware Request Model

```csharp
public class VoiceAwareCodeGenerationRequest : CodeGenerationRequest
{
    public ResponseFormat Format { get; set; } = ResponseFormat.Technical;
    public VoicePreferences? VoicePreferences { get; set; }
}

public enum ResponseFormat
{
    Technical,      // Current behavior
    Voice,          // Voice-optimized only
    Structured      // Both formats in structured response
}

public class VoicePreferences
{
    public int MaxSummaryWords { get; set; } = 50;
    public int MaxActions { get; set; } = 5;
    public bool IncludeNextSteps { get; set; } = true;
    public bool IncludeWarnings { get; set; } = true;
    public string ConversationStyle { get; set; } = "professional"; // professional, casual, concise
}
```

### 3. Enhanced Prompt Builder

```csharp
public class VoiceAwarePromptBuilder : IPromptBuilder
{
    public async Task<string> BuildPromptAsync(
        VoiceAwareCodeGenerationRequest request,
        ConversationContext context)
    {
        var basePrompt = await BuildBasePromptAsync(request, context);
        
        if (request.Format == ResponseFormat.Voice || 
            request.Format == ResponseFormat.Structured)
        {
            basePrompt += $"\n\n{VOICE_RESPONSE_INSTRUCTIONS}";
            
            if (request.Format == ResponseFormat.Structured)
            {
                basePrompt += @"
                
Please structure your response as follows:
```json
{
  ""voice_summary"": {
    ""brief"": ""1-2 sentence summary"",
    ""actions"": [""action1"", ""action2""],
    ""warnings"": [""warning1""],
    ""next_steps"": ""suggestion""
  },
  ""code"": ""```language\n// your code here\n```"",
  ""technical_explanation"": ""detailed explanation""
}
```";
            }
        }
        
        return basePrompt;
    }
}
```

### 4. Response Parser for Structured Format

```csharp
public class StructuredResponseParser
{
    public ParsedResponse Parse(string claudeResponse, ResponseFormat format)
    {
        if (format == ResponseFormat.Structured)
        {
            try
            {
                // Parse JSON structure
                var structured = JsonSerializer.Deserialize<StructuredResponse>(claudeResponse);
                return new ParsedResponse
                {
                    VoiceSummary = structured.VoiceSummary,
                    Code = structured.Code,
                    TechnicalExplanation = structured.TechnicalExplanation
                };
            }
            catch
            {
                // Fallback to regex parsing
                return ParseUnstructuredResponse(claudeResponse);
            }
        }
        
        return ParseUnstructuredResponse(claudeResponse);
    }
}
```

## Testing Voice Prompts

### Example 1: Simple Request
**User**: "Create a button component that says hello"

**Voice Response**:
> "I've created a simple button component that displays 'hello'. It's a functional React component using TypeScript. Would you like me to add click handling or styling?"

### Example 2: Complex Request
**User**: "Refactor the user service to use async await instead of promises"

**Voice Response**:
> "I've refactored your user service to use modern async/await syntax. This affected 5 methods: getUser, createUser, updateUser, deleteUser, and listUsers. The code is now cleaner and easier to read. You'll want to run your tests to ensure everything still works correctly."

### Example 3: Error Scenario
**User**: "Fix the bug in the payment processing"

**Voice Response**:
> "I found and fixed a race condition in your payment processor. The issue was with concurrent transaction handling. I've added proper locking mechanisms. Important: This requires testing with your payment gateway before deploying to production."

## Recommendation

I recommend the **Structured Response Approach** because it:
1. Gives Claude clear guidelines for voice-friendly output
2. Preserves full technical details for the UI
3. Allows progressive enhancement (can still work with non-voice-aware clients)
4. Enables A/B testing between different voice styles
5. Maintains backward compatibility

The orchestrator service can then simply extract the voice_summary portion when needed, making it a lightweight operation rather than complex parsing or additional AI calls.