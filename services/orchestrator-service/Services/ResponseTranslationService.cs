using System.Text.RegularExpressions;
using VoiceCode.OrchestratorService.Models;
using VoiceCode.OrchestratorService.Configuration;
using Microsoft.Extensions.Options;

namespace VoiceCode.OrchestratorService.Services;

public interface IResponseTranslationService
{
    Task<VoiceSummary> TranslateClaudeResponseAsync(
        string claudeResponse, 
        string originalRequest,
        ResponseDetailLevel detailLevel);
}

public class ResponseTranslationService : IResponseTranslationService
{
    private readonly ILogger<ResponseTranslationService> _logger;
    private readonly ICodeAnalysisService _codeAnalysis;
    private readonly OrchestrationOptions _options;

    public ResponseTranslationService(
        ILogger<ResponseTranslationService> logger,
        ICodeAnalysisService codeAnalysis,
        IOptions<OrchestrationOptions> options)
    {
        _logger = logger;
        _codeAnalysis = codeAnalysis;
        _options = options.Value;
    }

    public async Task<VoiceSummary> TranslateClaudeResponseAsync(
        string claudeResponse,
        string originalRequest,
        ResponseDetailLevel detailLevel)
    {
        try
        {
            _logger.LogInformation("Translating Claude response for voice output");

            var summary = new VoiceSummary();

            // Extract code blocks and explanations
            var codeBlocks = ExtractCodeBlocks(claudeResponse);
            var explanation = ExtractExplanation(claudeResponse);

            // Analyze code changes
            var codeChanges = await _codeAnalysis.AnalyzeCodeBlocksAsync(codeBlocks);

            // Generate brief description based on the original request and what was done
            summary.BriefDescription = GenerateBriefDescription(originalRequest, codeChanges, explanation);

            // Extract key actions performed
            summary.KeyActions = ExtractKeyActions(codeChanges, explanation);

            // Determine if confirmation is needed
            var confirmationNeeded = CheckIfConfirmationNeeded(codeChanges, originalRequest);
            if (confirmationNeeded)
            {
                summary.RequiresConfirmation = true;
                summary.Confirmation = GenerateConfirmationPrompt(codeChanges);
            }

            // Suggest next steps
            summary.NextSteps = GenerateNextSteps(codeChanges, originalRequest);

            // Add any warnings or important notes
            var warnings = ExtractWarnings(claudeResponse);
            if (warnings.Any())
            {
                summary.AdditionalContext["warnings"] = warnings;
            }

            // Adjust detail level
            summary = AdjustDetailLevel(summary, detailLevel);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating Claude response");
            return new VoiceSummary
            {
                BriefDescription = "I've completed the task but encountered an issue summarizing the results.",
                KeyActions = new List<string> { "Task completed with warnings" },
                RequiresConfirmation = true
            };
        }
    }

    private List<string> ExtractCodeBlocks(string response)
    {
        var codeBlocks = new List<string>();
        var codeBlockPattern = @"```[\w]*\n(.*?)\n```";
        var matches = Regex.Matches(response, codeBlockPattern, RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            codeBlocks.Add(match.Groups[1].Value.Trim());
        }

        return codeBlocks;
    }

    private string ExtractExplanation(string response)
    {
        // Remove code blocks to get just the explanation
        var withoutCode = Regex.Replace(response, @"```[\w]*\n.*?\n```", "", RegexOptions.Singleline);
        return withoutCode.Trim();
    }

    private string GenerateBriefDescription(string request, List<CodeChange> changes, string explanation)
    {
        // Start with understanding what was requested
        var action = DetermineAction(request);
        
        if (changes.Any())
        {
            var fileCount = changes.Count;
            var primaryChange = changes.First();

            if (fileCount == 1)
            {
                return $"I've {action} {primaryChange.Summary}";
            }
            else
            {
                return $"I've {action} across {fileCount} files, primarily {primaryChange.Summary}";
            }
        }

        // Fallback to extracting from explanation
        var firstSentence = explanation.Split('.').FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(firstSentence) 
            ? $"I've completed the {action} task"
            : firstSentence;
    }

    private string DetermineAction(string request)
    {
        var lowerRequest = request.ToLower();
        
        if (lowerRequest.Contains("create") || lowerRequest.Contains("add"))
            return "created";
        if (lowerRequest.Contains("update") || lowerRequest.Contains("modify") || lowerRequest.Contains("change"))
            return "updated";
        if (lowerRequest.Contains("fix") || lowerRequest.Contains("debug"))
            return "fixed";
        if (lowerRequest.Contains("refactor"))
            return "refactored";
        if (lowerRequest.Contains("delete") || lowerRequest.Contains("remove"))
            return "removed";
        
        return "completed";
    }

    private List<string> ExtractKeyActions(List<CodeChange> changes, string explanation)
    {
        var actions = new List<string>();

        // Add code changes as actions
        foreach (var change in changes.Take(_options.MaxKeyActions))
        {
            actions.Add($"{change.ChangeType} {change.FileName}: {change.Summary}");
        }

        // Extract action items from explanation using patterns
        var actionPatterns = new[]
        {
            @"(?:I've |I have |I |We've |We )(created|added|updated|modified|fixed|implemented|removed|deleted) ([^\.]+)",
            @"(?:The \w+ now )([\w\s]+)",
            @"(?:This )(creates|adds|updates|implements) ([^\.]+)"
        };

        foreach (var pattern in actionPatterns)
        {
            var matches = Regex.Matches(explanation, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (actions.Count < _options.MaxKeyActions)
                {
                    var action = match.Value.Trim();
                    if (!actions.Any(a => a.Contains(action)))
                    {
                        actions.Add(action);
                    }
                }
            }
        }

        return actions.Take(_options.MaxKeyActions).ToList();
    }

    private bool CheckIfConfirmationNeeded(List<CodeChange> changes, string request)
    {
        // Check for potentially destructive operations
        var destructiveKeywords = new[] { "delete", "remove", "drop", "truncate", "clear", "reset" };
        var requestLower = request.ToLower();
        
        if (destructiveKeywords.Any(keyword => requestLower.Contains(keyword)))
            return true;

        // Check for large-scale changes
        if (changes.Count > 5)
            return true;

        // Check for changes to critical files
        var criticalFiles = new[] { "package.json", "appsettings.json", ".env", "config", "database" };
        if (changes.Any(c => criticalFiles.Any(f => c.FileName.ToLower().Contains(f))))
            return true;

        return false;
    }

    private ConfirmationPrompt GenerateConfirmationPrompt(List<CodeChange> changes)
    {
        var prompt = new ConfirmationPrompt
        {
            Question = $"I'm about to make changes to {changes.Count} file(s). Should I proceed?",
            Options = new List<string> { "Yes, proceed", "Show me details first", "Cancel" },
            DefaultOption = "Show me details first"
        };

        if (changes.Any(c => c.ChangeType == "Deleted"))
        {
            prompt.WarningMessage = "This includes deleting files which cannot be undone.";
        }

        return prompt;
    }

    private string GenerateNextSteps(List<CodeChange> changes, string request)
    {
        var suggestions = new List<string>();

        // Check for common follow-up tasks
        if (changes.Any(c => c.FileName.EndsWith(".tsx") || c.FileName.EndsWith(".jsx")))
        {
            suggestions.Add("test the component");
            suggestions.Add("add styling");
        }

        if (changes.Any(c => c.KeyElements.Any(e => e.Contains("function") || e.Contains("method"))))
        {
            suggestions.Add("add unit tests");
            suggestions.Add("add documentation");
        }

        if (changes.Any(c => c.FileName.Contains("package.json")))
        {
            suggestions.Add("run npm install");
        }

        if (suggestions.Any())
        {
            return $"You might want to {string.Join(" or ", suggestions.Take(2))}.";
        }

        return null;
    }

    private List<string> ExtractWarnings(string response)
    {
        var warnings = new List<string>();
        
        var warningPatterns = new[]
        {
            @"(?:Warning|Note|Important|TODO|FIXME): ([^\n]+)",
            @"(?:Make sure|Don't forget|Remember) (?:to |that )([^\.\n]+)"
        };

        foreach (var pattern in warningPatterns)
        {
            var matches = Regex.Matches(response, pattern, RegexOptions.IgnoreCase);
            warnings.AddRange(matches.Select(m => m.Groups[1].Value.Trim()));
        }

        return warnings.Distinct().ToList();
    }

    private VoiceSummary AdjustDetailLevel(VoiceSummary summary, ResponseDetailLevel level)
    {
        switch (level)
        {
            case ResponseDetailLevel.Minimal:
                return new VoiceSummary
                {
                    BriefDescription = summary.BriefDescription,
                    RequiresConfirmation = summary.RequiresConfirmation,
                    Confirmation = summary.Confirmation
                };

            case ResponseDetailLevel.Detailed:
                // Add more technical details
                if (summary.AdditionalContext.ContainsKey("warnings"))
                {
                    summary.KeyActions.Add($"Note: {summary.AdditionalContext["warnings"]}");
                }
                break;

            case ResponseDetailLevel.Full:
                // Return everything
                break;

            case ResponseDetailLevel.Summary:
            default:
                // Return as-is
                break;
        }

        return summary;
    }
}