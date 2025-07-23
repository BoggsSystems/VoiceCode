using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class FileChange
{
    public string Path { get; set; } = string.Empty;
    public FileAction Action { get; set; }
    public string Content { get; set; } = string.Empty;
    public string OriginalContent { get; set; } = string.Empty;
    public List<DiffSegment> Diff { get; set; } = new();
    public List<string> ConflictMarkers { get; set; } = new();
    public FileMetadata Metadata { get; set; } = new();
}

public class DiffSegment
{
    public DiffOperation Operation { get; set; }
    public string Text { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public class FileMetadata
{
    public string MimeType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Encoding { get; set; } = "UTF-8";
    public DateTime LastModified { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

public class GeneratedFiles
{
    public string Id { get; set; } = string.Empty;
    public List<GeneratedFile> Files { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string CommitMessage { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public FileGenerationStatistics Statistics { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class FileGenerationStatistics
{
    public int FilesCreated { get; set; }
    public int FilesModified { get; set; }
    public int FilesDeleted { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesRemoved { get; set; }
    public Dictionary<string, int> FilesByLanguage { get; set; } = new();
}

public class GeneratedFile
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Template { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}