namespace VoiceCode.GeneratorService.Configuration;

public class GeneratorOptions
{
    public Dictionary<string, LanguageSettings> Languages { get; set; } = new();
    public int MaxFilesPerGeneration { get; set; } = 20;
    public int MaxFileSizeKb { get; set; } = 100;
    public bool ValidateGeneratedCode { get; set; } = true;
    public bool FormatGeneratedCode { get; set; } = true;
    public string DefaultFileEncoding { get; set; } = "UTF-8";
}

public class LanguageSettings
{
    public string FileExtension { get; set; } = string.Empty;
    public string DefaultIndentation { get; set; } = "    ";
    public List<string> StandardImports { get; set; } = new();
    public Dictionary<string, string> FileTemplates { get; set; } = new();
    public CodeStyle CodeStyle { get; set; } = new();
}

public class CodeStyle
{
    public string NamingConvention { get; set; } = "PascalCase";
    public bool UseExplicitTypes { get; set; } = true;
    public bool PreferAsync { get; set; } = true;
    public string LineEnding { get; set; } = "\n";
    public int MaxLineLength { get; set; } = 120;
}

public class TemplateOptions
{
    public string TemplatesPath { get; set; } = "Templates";
    public bool EnableCustomTemplates { get; set; } = true;
    public Dictionary<string, string> GlobalVariables { get; set; } = new();
    public bool CacheCompiledTemplates { get; set; } = true;
}