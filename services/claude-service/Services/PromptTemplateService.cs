using VoiceCode.Common.DTOs;

namespace VoiceCode.ClaudeService.Services;

public interface IPromptTemplateService
{
    Task<string> BuildPromptAsync(CodeGenerationRequest request);
    Task<string> GetSystemPromptAsync(string requestType);
}

public class PromptTemplateService : IPromptTemplateService
{
    private readonly ILogger<PromptTemplateService> _logger;
    private readonly Dictionary<string, string> _systemPrompts;
    private readonly Dictionary<string, Func<CodeGenerationRequest, string>> _promptBuilders;

    public PromptTemplateService(ILogger<PromptTemplateService> logger)
    {
        _logger = logger;
        _systemPrompts = InitializeSystemPrompts();
        _promptBuilders = InitializePromptBuilders();
    }

    public Task<string> BuildPromptAsync(CodeGenerationRequest request)
    {
        if (_promptBuilders.TryGetValue(request.Type.ToLower(), out var builder))
        {
            var prompt = builder(request);
            _logger.LogDebug("Built prompt for type: {Type}, Length: {Length}", request.Type, prompt.Length);
            return Task.FromResult(prompt);
        }

        // Default prompt builder
        var defaultPrompt = BuildDefaultPrompt(request);
        return Task.FromResult(defaultPrompt);
    }

    public Task<string> GetSystemPromptAsync(string requestType)
    {
        if (_systemPrompts.TryGetValue(requestType.ToLower(), out var systemPrompt))
        {
            return Task.FromResult(systemPrompt);
        }

        return Task.FromResult(_systemPrompts["default"]);
    }

    private Dictionary<string, string> InitializeSystemPrompts()
    {
        return new Dictionary<string, string>
        {
            ["default"] = @"You are an expert software developer and coding assistant. 
You write clean, efficient, and well-documented code following best practices.
You explain your code and decisions clearly.
Always consider security, performance, and maintainability.",

            ["generate"] = @"You are an expert software developer specializing in code generation.
Create production-ready code that follows these principles:
- Use appropriate design patterns
- Include error handling and validation
- Follow language-specific conventions and idioms
- Add clear, concise comments for complex logic
- Consider edge cases and potential failures",

            ["explain"] = @"You are a patient programming teacher and code reviewer.
When explaining code:
- Start with a high-level overview
- Break down complex parts step by step
- Use analogies when helpful
- Highlight important patterns or techniques
- Note any potential issues or improvements",

            ["fix"] = @"You are a debugging expert and problem solver.
When fixing code:
- First identify the root cause of the error
- Explain what was wrong and why
- Provide the corrected code
- Suggest how to prevent similar issues
- Maintain the original code's intent and style",

            ["refactor"] = @"You are a software architect focused on code quality.
When refactoring:
- Preserve all existing functionality
- Improve code readability and maintainability
- Apply SOLID principles where appropriate
- Reduce complexity and duplication
- Explain each refactoring decision",

            ["review"] = @"You are a thorough code reviewer.
Focus on:
- Security vulnerabilities
- Performance issues
- Code smells and anti-patterns
- Missing error handling
- Opportunities for improvement
Provide actionable feedback with examples.",

            ["test"] = @"You are a testing expert.
Create comprehensive tests that:
- Cover happy paths and edge cases
- Include meaningful assertions
- Follow testing best practices
- Are maintainable and readable
- Include both unit and integration tests where appropriate"
        };
    }

    private Dictionary<string, Func<CodeGenerationRequest, string>> InitializePromptBuilders()
    {
        return new Dictionary<string, Func<CodeGenerationRequest, string>>
        {
            ["generate"] = BuildGeneratePrompt,
            ["explain"] = BuildExplainPrompt,
            ["fix"] = BuildFixPrompt,
            ["refactor"] = BuildRefactorPrompt,
            ["review"] = BuildReviewPrompt,
            ["test"] = BuildTestPrompt
        };
    }

    private string BuildGeneratePrompt(CodeGenerationRequest request)
    {
        var prompt = $"Generate {request.Language} code for the following requirement:\n\n";
        prompt += request.Instructions + "\n\n";

        if (request.Context?.Any() == true)
        {
            prompt += "Context:\n";
            foreach (var (key, value) in request.Context)
            {
                prompt += $"- {key}: {value}\n";
            }
            prompt += "\n";
        }

        if (request.Examples?.Any() == true)
        {
            prompt += "Examples to follow:\n";
            foreach (var example in request.Examples)
            {
                prompt += $"```{request.Language}\n{example}\n```\n\n";
            }
        }

        prompt += "Requirements:\n";
        prompt += "- Use modern language features appropriately\n";
        prompt += "- Include error handling\n";
        prompt += "- Add descriptive variable and function names\n";
        prompt += "- Follow standard naming conventions\n";

        return prompt;
    }

    private string BuildExplainPrompt(CodeGenerationRequest request)
    {
        return $@"Please explain the following {request.Language} code in detail:

```{request.Language}
{request.Code}
```

Provide:
1. Overall purpose and functionality
2. Step-by-step breakdown of what the code does
3. Any notable patterns or techniques used
4. Potential issues or areas for improvement";
    }

    private string BuildFixPrompt(CodeGenerationRequest request)
    {
        var errorInfo = request.Context?.GetValueOrDefault("error")?.ToString() ?? "Unknown error";
        
        return $@"Fix the following {request.Language} code that has this error:

Error: {errorInfo}

Code:
```{request.Language}
{request.Code}
```

Please:
1. Identify the cause of the error
2. Provide the corrected code
3. Explain what was wrong and how you fixed it
4. Suggest how to avoid this error in the future";
    }

    private string BuildRefactorPrompt(CodeGenerationRequest request)
    {
        return $@"Refactor the following {request.Language} code according to these instructions:

Instructions: {request.Instructions}

Current code:
```{request.Language}
{request.Code}
```

Please:
1. Apply the requested refactoring
2. Maintain all existing functionality
3. Explain each change you made
4. Highlight any improvements in readability, performance, or maintainability";
    }

    private string BuildReviewPrompt(CodeGenerationRequest request)
    {
        return $@"Review the following {request.Language} code:

```{request.Language}
{request.Code}
```

Provide feedback on:
1. Code quality and readability
2. Potential bugs or errors
3. Security concerns
4. Performance considerations
5. Suggestions for improvement

Be specific and provide examples where applicable.";
    }

    private string BuildTestPrompt(CodeGenerationRequest request)
    {
        return $@"Create comprehensive tests for the following {request.Language} code:

```{request.Language}
{request.Code}
```

Include:
1. Unit tests for individual functions/methods
2. Edge case testing
3. Error condition testing
4. Integration tests if applicable
5. Clear test descriptions

Use the appropriate testing framework for {request.Language}.";
    }

    private string BuildDefaultPrompt(CodeGenerationRequest request)
    {
        var prompt = $"Process this {request.Language} code request:\n\n";
        
        if (!string.IsNullOrEmpty(request.Instructions))
        {
            prompt += $"Instructions: {request.Instructions}\n\n";
        }

        if (!string.IsNullOrEmpty(request.Code))
        {
            prompt += $"Code:\n```{request.Language}\n{request.Code}\n```\n\n";
        }

        prompt += "Provide a clear and helpful response.";

        return prompt;
    }
}