# VoiceCode - AI-Powered Voice Assistant Pair Programmer

VoiceCode is a cloud-native voice-controlled coding assistant that leverages Claude AI to generate, edit, and manage code through natural language commands. Built on Microsoft Azure using a microservice architecture.

## 🎯 Overview

VoiceCode enables developers to:
- Speak natural language commands to generate or modify code
- Receive AI-powered code suggestions and implementations
- Automatically apply changes to local development environments
- Get voice feedback on completed actions

## 🏗️ Architecture

### High-Level Components

1. **Web Application** - Cross-platform voice input and response display
2. **Azure Cloud Services** - Processing pipeline and AI integration
3. **Local Mac Agent** - File system integration and IDE control
4. **Claude AI Integration** - Code generation and analysis

### Key Features

- Real-time voice-to-code conversion
- Context-aware code generation
- Multi-platform support (iOS + macOS)
- Secure cloud processing
- Scalable microservice architecture

## 📁 Project Structure

```
VoiceCode/
├── docs/                    # Architecture and design documentation
├── services/               # Microservices source code
│   ├── stt-service/       # Speech-to-Text service
│   ├── claude-service/    # Claude AI integration
│   ├── prompt-router/     # Prompt routing and context
│   ├── code-generator/    # Code file generation
│   ├── tts-service/       # Text-to-Speech service
│   └── dispatcher/        # Result dispatcher
├── infrastructure/         # Azure infrastructure as code
├── mac-agent/             # Local Mac agent
├── web-app/               # React/TypeScript web application
├── shared/                # Shared libraries and models
└── scripts/               # Deployment and utility scripts
```

## 🚀 Getting Started

See [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) for setup instructions.

## 📖 Documentation

- [Architecture Overview](docs/ARCHITECTURE.md)
- [Service Documentation](docs/services/)
- [API Reference](docs/api/)
- [Security Guide](docs/SECURITY.md)
- [Deployment Guide](docs/DEPLOYMENT.md)

## 🛡️ Security

VoiceCode implements enterprise-grade security:
- Azure Managed Identity for service authentication
- Key Vault for secrets management
- End-to-end encryption for voice data
- Role-based access control (RBAC)

## 📊 Technology Stack

- **Cloud Platform**: Microsoft Azure
- **Backend Services**: C#/.NET 8, Azure Web Apps
- **Web App**: React, TypeScript, Tailwind CSS
- **Mac Agent**: .NET MAUI/C#
- **AI Integration**: Claude API
- **Infrastructure**: Terraform/Bicep

## 📄 License

See [LICENSE](LICENSE) for details.