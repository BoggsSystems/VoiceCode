using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Text;
using System.Text.RegularExpressions;

namespace VoiceCode.GeneratorService.Services;

public interface ICodeFormatter
{
    Task<string> FormatAsync(string code, string language);
}

public class CodeFormatterService : ICodeFormatter
{
    private readonly ILogger<CodeFormatterService> _logger;

    public CodeFormatterService(ILogger<CodeFormatterService> logger)
    {
        _logger = logger;
    }

    public async Task<string> FormatAsync(string code, string language)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        try
        {
            return language.ToLower() switch
            {
                "csharp" or "c#" => await FormatCSharpAsync(code),
                "javascript" or "js" => await FormatJavaScriptAsync(code),
                "typescript" or "ts" => await FormatTypeScriptAsync(code),
                "python" or "py" => await FormatPythonAsync(code),
                "java" => await FormatJavaAsync(code),
                "json" => await FormatJsonAsync(code),
                "xml" => await FormatXmlAsync(code),
                "sql" => await FormatSqlAsync(code),
                _ => code
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to format {Language} code", language);
            return code;
        }
    }

    private async Task<string> FormatCSharpAsync(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();

        var workspace = new AdhocWorkspace();
        var formattedNode = Formatter.Format(root, workspace);

        return formattedNode.ToFullString();
    }

    private async Task<string> FormatJavaScriptAsync(string code)
    {
        var formatted = new StringBuilder();
        var lines = code.Split('\n');
        int indentLevel = 0;
        const string indent = "  ";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) 
            {
                formatted.AppendLine();
                continue;
            }

            // Decrease indent for closing braces
            if (trimmed.StartsWith('}') || trimmed.StartsWith(']') || trimmed.StartsWith(')'))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            // Apply indentation
            var indented = new string(' ', indentLevel * indent.Length) + trimmed;
            formatted.AppendLine(indented);

            // Increase indent for opening braces
            if (trimmed.EndsWith('{') || trimmed.EndsWith('[') || 
                (trimmed.EndsWith('(') && !trimmed.Contains(')')))
            {
                indentLevel++;
            }

            // Handle single-line arrow functions
            if (trimmed.Contains("=>") && !trimmed.Contains('{'))
            {
                // No indent change for single-line arrow functions
            }
            else if (trimmed.EndsWith("=>"))
            {
                indentLevel++;
            }
        }

        return await Task.FromResult(formatted.ToString().TrimEnd());
    }

    private async Task<string> FormatTypeScriptAsync(string code)
    {
        // TypeScript formatting is similar to JavaScript with additional handling for types
        var jsFormatted = await FormatJavaScriptAsync(code);
        
        // Additional TypeScript-specific formatting
        jsFormatted = Regex.Replace(jsFormatted, @":\s*([^,\s}]+)", ": $1");
        jsFormatted = Regex.Replace(jsFormatted, @"<\s*([^>]+)\s*>", "<$1>");
        
        return jsFormatted;
    }

    private async Task<string> FormatPythonAsync(string code)
    {
        var formatted = new StringBuilder();
        var lines = code.Split('\n');
        int indentLevel = 0;
        const string indent = "    ";

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                formatted.AppendLine();
                continue;
            }

            // Calculate current line's indentation
            var currentIndent = 0;
            foreach (var ch in line)
            {
                if (ch == ' ') currentIndent++;
                else if (ch == '\t') currentIndent += 4;
                else break;
            }

            // Detect dedent
            if (currentIndent < indentLevel * 4 && !string.IsNullOrWhiteSpace(trimmed))
            {
                indentLevel = currentIndent / 4;
            }

            // Apply proper indentation
            var properlyIndented = new string(' ', indentLevel * 4) + trimmed.TrimStart();
            formatted.AppendLine(properlyIndented);

            // Increase indent after colons
            if (trimmed.EndsWith(':'))
            {
                indentLevel++;
            }
        }

        return await Task.FromResult(formatted.ToString().TrimEnd());
    }

    private async Task<string> FormatJavaAsync(string code)
    {
        var formatted = new StringBuilder();
        var lines = code.Split('\n');
        int indentLevel = 0;
        const string indent = "    ";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                formatted.AppendLine();
                continue;
            }

            // Decrease indent for closing braces
            if (trimmed.StartsWith('}'))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            // Apply indentation
            var indented = new string(' ', indentLevel * indent.Length) + trimmed;
            formatted.AppendLine(indented);

            // Increase indent for opening braces
            if (trimmed.EndsWith('{'))
            {
                indentLevel++;
            }

            // Handle case statements
            if (trimmed.StartsWith("case ") || trimmed.Equals("default:"))
            {
                indentLevel++;
            }
            else if (trimmed.Equals("break;") && indentLevel > 0)
            {
                indentLevel--;
            }
        }

        return await Task.FromResult(formatted.ToString().TrimEnd());
    }

    private async Task<string> FormatJsonAsync(string code)
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<object>(code);
            return System.Text.Json.JsonSerializer.Serialize(parsed, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            return code;
        }
    }

    private async Task<string> FormatXmlAsync(string code)
    {
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(code);

            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = System.Xml.NewLineHandling.Replace
            };

            using var stringWriter = new StringWriter();
            using var xmlWriter = System.Xml.XmlWriter.Create(stringWriter, settings);
            doc.Save(xmlWriter);
            return stringWriter.ToString();
        }
        catch
        {
            return code;
        }
    }

    private async Task<string> FormatSqlAsync(string code)
    {
        // Basic SQL formatting
        var keywords = new[]
        {
            "SELECT", "FROM", "WHERE", "JOIN", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN",
            "ORDER BY", "GROUP BY", "HAVING", "INSERT INTO", "VALUES", "UPDATE", "SET",
            "DELETE FROM", "CREATE TABLE", "ALTER TABLE", "DROP TABLE", "AND", "OR"
        };

        var formatted = code;
        
        // Add newlines before major keywords
        foreach (var keyword in keywords)
        {
            formatted = Regex.Replace(formatted, $@"\s+{keyword}\s+", $"\n{keyword} ", RegexOptions.IgnoreCase);
        }

        // Indent subqueries
        formatted = Regex.Replace(formatted, @"\(\s*SELECT", "(\n  SELECT", RegexOptions.IgnoreCase);
        
        // Clean up multiple spaces and newlines
        formatted = Regex.Replace(formatted, @"\s+", " ");
        formatted = Regex.Replace(formatted, @"\n\s*\n", "\n");
        
        return await Task.FromResult(formatted.Trim());
    }
}