# VoiceCode.Common

This is the shared library containing all common models, interfaces, DTOs, and utilities used across VoiceCode services.

## Structure

- **Models/** - Domain models used throughout the system
- **Interfaces/** - Service and repository interfaces
- **DTOs/** - Data Transfer Objects for API requests/responses
- **Enums/** - All system enumerations
- **Constants/** - System-wide constants
- **Exceptions/** - Custom exception types
- **Extensions/** - Extension methods for common operations
- **Validation/** - Validation rules and constraints

## Usage

Add reference to this project in your service:

```xml
<ProjectReference Include="..\..\..\shared\VoiceCode.Common\VoiceCode.Common.csproj" />
```

## Key Features

### Models
- User and authentication models
- Voice processing models
- Code generation models
- File management models
- Processing pipeline models

### Interfaces
- Service interfaces (ISTTService, IClaudeService, etc.)
- Repository interfaces
- External service interfaces

### Extensions
- String extensions (hashing, sanitization, validation)
- DateTime extensions (relative time, time calculations)
- Collection extensions (batch processing, async operations)

### Validation Rules
- Audio file validation
- Code instruction validation
- File path validation
- API rate limiting rules

### Custom Exceptions
- VoiceCodeException base class
- Specific exceptions for different error scenarios
- Rich error details and error codes

## Building

```bash
dotnet build
```

## Testing

```bash
dotnet test
```