# VoiceCode Generator Service

Code generation orchestration service that processes Claude AI responses into organized, validated, and formatted code files.

## Features

- **Multi-Language Support**: Built-in processors for C#, TypeScript, JavaScript, Python, and Java
- **Code Organization**: Automatically organizes generated files into appropriate directory structures
- **Code Validation**: Language-specific syntax validation using Roslyn for C# and pattern matching for other languages
- **Code Formatting**: Automatic code formatting with language-specific rules
- **Template Engine**: Scriban-based template system for code generation patterns
- **File Management**: Smart file naming and organization based on code content
- **Queue Processing**: Processes code generation requests from Azure Service Bus
- **Extensible Architecture**: Easy to add new language processors

## Architecture

### Components

1. **Language Processors**: Language-specific code processing logic
2. **Code Validator**: Validates generated code for syntax and common issues
3. **Code Formatter**: Formats code according to language conventions
4. **File Organizer**: Organizes files into proper project structure
5. **Template Engine**: Renders code templates with variables
6. **Queue Processor**: Handles incoming generation requests

### Supported Languages

- **C#**: Full Roslyn-based processing, validation, and formatting
- **TypeScript/JavaScript**: Module detection and formatting
- **Python**: PEP-8 style formatting and validation
- **Java**: Class-based organization and validation

## Prerequisites

- .NET 8 SDK
- Azure Service Bus namespace
- Redis instance
- Access to Claude Service API

## Configuration

### Development

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:ServiceBus" "your-service-bus-connection"
dotnet user-secrets set "ClaudeService:BaseUrl" "http://localhost:5002"
```

### Production

Environment variables:
- `ConnectionStrings__ServiceBus`: Service Bus connection
- `ConnectionStrings__Redis`: Redis connection
- `ClaudeService__BaseUrl`: Claude service URL
- `ApplicationInsights__ConnectionString`: Application Insights

## API Endpoints

### POST /api/generator/generate
Generate files from code blocks.

**Request:**
```json
{
  "codeBlocks": [
    {
      "language": "csharp",
      "content": "public class User { ... }",
      "fileName": "User.cs"
    }
  ],
  "targetDirectory": "src"
}
```

**Response:**
```json
{
  "id": "gen-123",
  "files": [
    {
      "fileName": "User.cs",
      "filePath": "src/Models/User.cs",
      "content": "formatted code...",
      "size": 1024
    }
  ],
  "errors": []
}
```

### POST /api/generator/validate
Validate code syntax.

### POST /api/generator/format
Format code according to language conventions.

### POST /api/generator/preview
Preview file generation without saving.

### GET /api/generator/languages
Get list of supported languages.

### GET /api/generator/templates
Get available code templates.

## File Organization

The service automatically organizes files based on their content:

### C# Project Structure
```
src/
├── Controllers/
├── Models/
├── Services/
├── Interfaces/
└── Tests/
```

### TypeScript/JavaScript Structure
```
src/
├── components/
├── services/
├── models/
├── interfaces/
└── tests/
```

### Python Structure
```
src/
├── models/
├── services/
├── controllers/
└── tests/
```

## Template System

Create custom templates using Scriban syntax:

```liquid
namespace {{ namespace }}
{
    public class {{ class_name | pascal_case }}
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
```

Built-in template functions:
- `pascal_case`: Convert to PascalCase
- `camel_case`: Convert to camelCase
- `snake_case`: Convert to snake_case
- `kebab_case`: Convert to kebab-case
- `pluralize`: Pluralize words
- `singularize`: Singularize words

## Queue Processing

The service processes messages from the `code-generation` queue with subjects:
- `generate`: Generate new code
- `explain`: Explain existing code
- `fix`: Fix code errors
- `refactor`: Refactor code

Results are sent to the `dispatcher` queue for delivery to clients.

## Running Locally

```bash
cd services/generator-service
dotnet run
```

Service available at https://localhost:7004 (or http://localhost:5004).

## Testing

```bash
dotnet test
```

## Docker

Build:
```bash
docker build -f services/generator-service/Dockerfile -t voicecode-generator-service .
```

Run:
```bash
docker run -d -p 80:80 \
  -e ConnectionStrings__ServiceBus="your-connection" \
  -e ConnectionStrings__Redis="redis-connection" \
  -e ClaudeService__BaseUrl="http://claude-service" \
  voicecode-generator-service
```

## Extending

### Adding a New Language Processor

1. Create a new processor class:
```csharp
public class GoProcessor : ILanguageProcessor
{
    public string Language => "go";
    
    public async Task<List<GeneratedFile>> ProcessAsync(CodeBlock codeBlock)
    {
        // Implementation
    }
}
```

2. Register in Program.cs:
```csharp
builder.Services.AddScoped<ILanguageProcessor, GoProcessor>();
```

3. Add to ProcessorFactory mapping

## Performance

- Template caching for improved performance
- Concurrent file processing
- Redis caching for repeated operations
- Configurable file size and count limits