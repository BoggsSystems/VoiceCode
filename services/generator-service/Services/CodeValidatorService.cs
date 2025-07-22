using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.GeneratorService.Services;

public interface ICodeValidator
{
    Task<ValidationResult> ValidateAsync(string code, string language);
    Task<List<ValidationIssue>> GetDetailedIssuesAsync(string code, string language);
}

public class CodeValidatorService : ICodeValidator
{
    private readonly ILogger<CodeValidatorService> _logger;

    public CodeValidatorService(ILogger<CodeValidatorService> logger)
    {
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(string code, string language)
    {
        try
        {
            var issues = await GetDetailedIssuesAsync(code, language);
            var hasErrors = issues.Any(i => i.Severity == IssueSeverity.Error);

            return new ValidationResult
            {
                IsValid = !hasErrors,
                Error = hasErrors ? "Code contains errors" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for language {Language}", language);
            return new ValidationResult
            {
                IsValid = false,
                Error = $"Validation error: {ex.Message}"
            };
        }
    }

    public async Task<List<ValidationIssue>> GetDetailedIssuesAsync(string code, string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => await ValidateCSharpAsync(code),
            "javascript" or "js" => await ValidateJavaScriptAsync(code),
            "typescript" or "ts" => await ValidateTypeScriptAsync(code),
            "python" or "py" => await ValidatePythonAsync(code),
            "java" => await ValidateJavaAsync(code),
            _ => await ValidateGenericAsync(code)
        };
    }

    private async Task<List<ValidationIssue>> ValidateCSharpAsync(string code)
    {
        var issues = new List<ValidationIssue>();

        try
        {
            // Parse with Roslyn
            var tree = CSharpSyntaxTree.ParseText(code);
            var diagnostics = tree.GetDiagnostics();

            foreach (var diagnostic in diagnostics)
            {
                issues.Add(new ValidationIssue
                {
                    Message = diagnostic.GetMessage(),
                    Severity = MapRoslynSeverity(diagnostic.Severity),
                    Line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1,
                    Column = diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1,
                    Code = diagnostic.Id
                });
            }

            // Additional C# specific checks
            if (code.Contains("unsafe"))
            {
                issues.Add(new ValidationIssue
                {
                    Message = "Unsafe code detected",
                    Severity = IssueSeverity.Warning,
                    Code = "CS0001"
                });
            }

            // Check for common issues
            AddCommonCSharpIssues(code, issues);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "C# validation error");
            issues.Add(new ValidationIssue
            {
                Message = "Failed to parse C# code",
                Severity = IssueSeverity.Error
            });
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ValidationIssue>> ValidateJavaScriptAsync(string code)
    {
        var issues = new List<ValidationIssue>();

        // Basic JavaScript validation
        try
        {
            // Check for syntax errors using regex patterns
            if (!CheckBalancedBraces(code))
            {
                issues.Add(new ValidationIssue
                {
                    Message = "Unbalanced braces detected",
                    Severity = IssueSeverity.Error
                });
            }

            // Check for common JavaScript issues
            if (Regex.IsMatch(code, @"==(?!=)"))
            {
                issues.Add(new ValidationIssue
                {
                    Message = "Use === instead of == for comparisons",
                    Severity = IssueSeverity.Warning,
                    Code = "JS001"
                });
            }

            if (code.Contains("var "))
            {
                issues.Add(new ValidationIssue
                {
                    Message = "Consider using 'let' or 'const' instead of 'var'",
                    Severity = IssueSeverity.Info,
                    Code = "JS002"
                });
            }

            // Check for missing semicolons in obvious places
            var lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length > 0 && !line.EndsWith(';') && !line.EndsWith('{') && !line.EndsWith('}') &&
                    !line.StartsWith("//") && !line.StartsWith("*") && !line.Contains("=>") &&
                    Regex.IsMatch(line, @"^\s*(let|const|var|return|throw|break|continue)\s"))
                {
                    issues.Add(new ValidationIssue
                    {
                        Message = "Missing semicolon",
                        Severity = IssueSeverity.Warning,
                        Line = i + 1,
                        Code = "JS003"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JavaScript validation error");
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ValidationIssue>> ValidateTypeScriptAsync(string code)
    {
        // Start with JavaScript validation
        var issues = await ValidateJavaScriptAsync(code);

        // Add TypeScript specific checks
        if (!code.Contains(": ") && !code.Contains("interface") && !code.Contains("type"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Consider adding type annotations",
                Severity = IssueSeverity.Info,
                Code = "TS001"
            });
        }

        if (code.Contains(": any"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Avoid using 'any' type",
                Severity = IssueSeverity.Warning,
                Code = "TS002"
            });
        }

        return issues;
    }

    private async Task<List<ValidationIssue>> ValidatePythonAsync(string code)
    {
        var issues = new List<ValidationIssue>();

        try
        {
            // Check indentation
            var lines = code.Split('\n');
            int expectedIndent = 0;
            var indentStack = new Stack<int>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var currentIndent = line.TakeWhile(char.IsWhiteSpace).Count();
                
                if (line.TrimStart().StartsWith("def ") || line.TrimStart().StartsWith("class ") ||
                    line.TrimStart().StartsWith("if ") || line.TrimStart().StartsWith("for ") ||
                    line.TrimStart().StartsWith("while ") || line.TrimStart().StartsWith("with "))
                {
                    if (line.TrimEnd().EndsWith(":"))
                    {
                        indentStack.Push(currentIndent);
                        expectedIndent = currentIndent + 4;
                    }
                }
                else if (currentIndent < expectedIndent && indentStack.Any())
                {
                    while (indentStack.Any() && indentStack.Peek() >= currentIndent)
                    {
                        indentStack.Pop();
                    }
                    expectedIndent = indentStack.Any() ? indentStack.Peek() + 4 : 0;
                }

                // Check for tabs
                if (line.Contains('\t'))
                {
                    issues.Add(new ValidationIssue
                    {
                        Message = "Use spaces instead of tabs",
                        Severity = IssueSeverity.Warning,
                        Line = i + 1,
                        Code = "PY001"
                    });
                }
            }

            // Check for Python 2 print statements
            if (Regex.IsMatch(code, @"print\s+[^(]"))
            {
                issues.Add(new ValidationIssue
                {
                    Message = "Use print() function (Python 3 syntax)",
                    Severity = IssueSeverity.Error,
                    Code = "PY002"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Python validation error");
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ValidationIssue>> ValidateJavaAsync(string code)
    {
        var issues = new List<ValidationIssue>();

        // Basic Java validation
        if (!CheckBalancedBraces(code))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Unbalanced braces detected",
                Severity = IssueSeverity.Error
            });
        }

        // Check for class declaration
        if (!Regex.IsMatch(code, @"(public|private|protected)?\s*class\s+\w+"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "No class declaration found",
                Severity = IssueSeverity.Warning,
                Code = "JAVA001"
            });
        }

        // Check for main method if it looks like an entry point
        if (code.Contains("public static void main") && !Regex.IsMatch(code, @"public\s+static\s+void\s+main\s*\(\s*String\s*\[\s*\]\s*\w+\s*\)"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Main method signature appears incorrect",
                Severity = IssueSeverity.Error,
                Code = "JAVA002"
            });
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ValidationIssue>> ValidateGenericAsync(string code)
    {
        var issues = new List<ValidationIssue>();

        // Basic syntax checks
        if (!CheckBalancedBraces(code))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Unbalanced braces or brackets detected",
                Severity = IssueSeverity.Warning
            });
        }

        // Check for very long lines
        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 120)
            {
                issues.Add(new ValidationIssue
                {
                    Message = "Line exceeds 120 characters",
                    Severity = IssueSeverity.Info,
                    Line = i + 1
                });
            }
        }

        return await Task.FromResult(issues);
    }

    private bool CheckBalancedBraces(string code)
    {
        var stack = new Stack<char>();
        var pairs = new Dictionary<char, char>
        {
            { '(', ')' },
            { '[', ']' },
            { '{', '}' }
        };

        bool inString = false;
        bool inComment = false;
        char stringChar = '\0';

        for (int i = 0; i < code.Length; i++)
        {
            var ch = code[i];

            // Handle strings
            if ((ch == '"' || ch == '\'') && !inComment && (i == 0 || code[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = ch;
                }
                else if (ch == stringChar)
                {
                    inString = false;
                }
                continue;
            }

            // Handle comments
            if (!inString && i < code.Length - 1)
            {
                if (code[i] == '/' && code[i + 1] == '/')
                {
                    inComment = true;
                    continue;
                }
                if (code[i] == '/' && code[i + 1] == '*')
                {
                    inComment = true;
                    continue;
                }
                if (inComment && code[i] == '*' && code[i + 1] == '/')
                {
                    inComment = false;
                    i++;
                    continue;
                }
            }

            if (ch == '\n')
            {
                inComment = false;
            }

            if (inString || inComment) continue;

            if (pairs.ContainsKey(ch))
            {
                stack.Push(ch);
            }
            else if (pairs.ContainsValue(ch))
            {
                if (stack.Count == 0) return false;
                var open = stack.Pop();
                if (pairs[open] != ch) return false;
            }
        }

        return stack.Count == 0;
    }

    private void AddCommonCSharpIssues(string code, List<ValidationIssue> issues)
    {
        // Check for common C# issues
        if (Regex.IsMatch(code, @"catch\s*\(\s*Exception\s*\)"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Avoid catching generic Exception",
                Severity = IssueSeverity.Warning,
                Code = "CS1001"
            });
        }

        if (code.Contains("Thread.Sleep"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Consider using async/await instead of Thread.Sleep",
                Severity = IssueSeverity.Info,
                Code = "CS1002"
            });
        }

        if (Regex.IsMatch(code, @"public\s+\w+\s+\w+\s*{[^}]*get[^}]*set[^}]*}") && 
            !code.Contains("{ get; set; }"))
        {
            issues.Add(new ValidationIssue
            {
                Message = "Consider using auto-implemented properties",
                Severity = IssueSeverity.Info,
                Code = "CS1003"
            });
        }
    }

    private IssueSeverity MapRoslynSeverity(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Error => IssueSeverity.Error,
            DiagnosticSeverity.Warning => IssueSeverity.Warning,
            DiagnosticSeverity.Info => IssueSeverity.Info,
            _ => IssueSeverity.Info
        };
    }
}

public class ValidationIssue
{
    public string Message { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? Code { get; set; }
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}