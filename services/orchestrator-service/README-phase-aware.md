# Phase-Aware Orchestration System

## Overview

The Phase-Aware Orchestration System transforms VoiceCode from a simple voice-to-code tool into a comprehensive software development assistant that guides users through the entire journey from ideation to implementation.

## Conversation Phases

### 1. **Ideation Phase**
- **Purpose**: Explore and validate business ideas
- **AI Analysis**: Market opportunity, competitors, risks, feasibility
- **Example**: "I'm thinking about an API platform for USDC cross-border transfers"
- **Output**: Comprehensive business analysis with feasibility score

### 2. **Product Design Phase**
- **Purpose**: Define product vision and features
- **AI Analysis**: User personas, core features, user flows, success metrics
- **Trigger**: User agrees to proceed after ideation
- **Output**: Product specification document

### 3. **Technical Design Phase**
- **Purpose**: Design technical implementation
- **AI Analysis**: Tech stack, API design, data models, security requirements
- **Trigger**: Product design completion
- **Output**: Technical specification with detailed schemas

### 4. **Architecture Phase**
- **Purpose**: Define system architecture
- **AI Analysis**: Architecture pattern, scaling strategy, infrastructure choices
- **Trigger**: Technical design completion
- **Output**: Architecture blueprint with deployment strategy

### 5. **Implementation Phase**
- **Purpose**: Build the actual solution
- **Action**: Routes to worker with full context from all phases
- **Trigger**: "Let's build it" or similar
- **Output**: Working code implementation

### 6. **Enhancement Phase**
- **Purpose**: Add features to existing implementations
- **Action**: Routes to assigned worker with enhancement request
- **Trigger**: References to existing project
- **Output**: Updated implementation

## Key Features

### Session Management
- Persistent conversation sessions across phases
- Automatic session detection from voice commands
- Context accumulation through phases
- Session history and status tracking

### Intelligent Routing
```
New Idea Indicators → Start Ideation
Continuation Keywords → Find Existing Session
Project References → Route to Worker
Phase Triggers → Advance to Next Phase
```

### Context Preservation
All design decisions flow to implementation:
- Business requirements → Implementation constraints
- Product features → Code features
- Technical design → Actual APIs and models
- Architecture → Project structure

## API Endpoints

### Process Voice Command
```
POST /api/v2/orchestrate/voice-command
{
  "userId": "user123",
  "transcribedText": "I want to build a forex trading platform",
  "audioUrl": "https://..."
}
```

### Get User Sessions
```
GET /api/v2/orchestrate/sessions?userId=user123
```

### Get Session Details
```
GET /api/v2/orchestrate/sessions/{sessionId}
```

### Continue Session
```
POST /api/v2/orchestrate/sessions/{sessionId}/continue
{
  "command": "Add user authentication"
}
```

## Usage Examples

### Starting a New Project
```
User: "I'm thinking about an API platform that enables fast, low-cost USDC transfers"
System: [Performs business analysis]
User: "Sounds good, let's design it"
System: [Creates product design]
User: "Ready for technical design"
System: [Generates technical specifications]
User: "Let's see the architecture"
System: [Recommends architecture]
User: "Build it"
System: [Routes to worker with full context]
```

### Continuing Existing Work
```
User: "How's my forex platform coming along?"
System: [Finds session, checks worker status]
User: "Add real-time rate updates"
System: [Routes enhancement to existing worker]
```

## Configuration

### Required Environment Variables
```
OpenAI__ApiKey=<your-api-key>
ServiceBus__FullyQualifiedNamespace=<service-bus-namespace>
```

### Optional Configuration
```json
{
  "OpenAI": {
    "Model": "gpt-4",
    "Temperature": 0.7
  },
  "SessionStorage": {
    "ExpirationHours": 24
  }
}
```

## Architecture

```
Voice Command
    ↓
Phase Detection
    ↓
Session Management ←→ Memory Cache
    ↓
Phase Processing
    ↓
ChatGPT Analysis ←→ OpenAI API
    ↓
Context Accumulation
    ↓
Worker Routing (if Implementation)
```

## Benefits

1. **Structured Development**: Guides users through proven software development methodology
2. **Context Preservation**: No loss of design decisions between phases
3. **Flexibility**: Can skip phases or return to previous work
4. **AI-Powered Analysis**: Each phase leverages specialized AI prompts
5. **Seamless Implementation**: Workers receive complete context for accurate implementation

## Future Enhancements

- Redis backing for distributed session storage
- Voice feedback at each phase
- Collaborative sessions (multiple users)
- Phase-specific UI visualizations
- Export design documents
- Integration with project management tools