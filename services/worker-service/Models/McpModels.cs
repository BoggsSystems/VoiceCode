using Newtonsoft.Json;

namespace VoiceCode.WorkerService.Models;

// MCP JSON-RPC 2.0 Messages
public class McpRequest
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [JsonProperty("method")]
    public string Method { get; set; } = string.Empty;
    
    [JsonProperty("params")]
    public object? Params { get; set; }
}

public class McpResponse
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonProperty("id")]
    public string? Id { get; set; }
    
    [JsonProperty("result")]
    public object? Result { get; set; }
    
    [JsonProperty("error")]
    public McpError? Error { get; set; }
}

public class McpError
{
    [JsonProperty("code")]
    public int Code { get; set; }
    
    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonProperty("data")]
    public object? Data { get; set; }
}

// MCP Protocol Messages
public class InitializeParams
{
    [JsonProperty("protocolVersion")]
    public string ProtocolVersion { get; set; } = "1.0";
    
    [JsonProperty("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();
    
    [JsonProperty("clientInfo")]
    public ClientInfo ClientInfo { get; set; } = new();
}

public class ClientCapabilities
{
    [JsonProperty("tools")]
    public ToolsCapability? Tools { get; set; } = new();
}

public class ToolsCapability
{
    [JsonProperty("call")]
    public bool Call { get; set; } = true;
}

public class ClientInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = "VoiceCode Worker Service";
    
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0.0";
}

public class ServerCapabilities
{
    [JsonProperty("tools")]
    public ToolsCapability? Tools { get; set; }
    
    [JsonProperty("prompts")]
    public PromptsCapability? Prompts { get; set; }
}

public class PromptsCapability
{
    [JsonProperty("list")]
    public bool List { get; set; }
    
    [JsonProperty("get")]
    public bool Get { get; set; }
}

// Tool-related models
public class Tool
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonProperty("inputSchema")]
    public object? InputSchema { get; set; }
}

public class ToolCallParams
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}

public class ToolCallResult
{
    [JsonProperty("content")]
    public List<ContentBlock> Content { get; set; } = new();
    
    [JsonProperty("isError")]
    public bool IsError { get; set; }
}

public class ContentBlock
{
    [JsonProperty("type")]
    public string Type { get; set; } = "text";
    
    [JsonProperty("text")]
    public string? Text { get; set; }
}