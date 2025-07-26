# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["VoiceCode.sln", "."]
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]
COPY ["services/stt-service/VoiceCode.STTService.csproj", "services/stt-service/"]

# Restore dependencies
RUN dotnet restore "services/stt-service/VoiceCode.STTService.csproj"

# Copy source code
COPY ["shared/VoiceCode.Common/", "shared/VoiceCode.Common/"]
COPY ["services/stt-service/", "services/stt-service/"]

# Build and publish
WORKDIR "/src/services/stt-service"
RUN dotnet build "VoiceCode.STTService.csproj" -c Release -o /app/build
RUN dotnet publish "VoiceCode.STTService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install dependencies for Speech SDK
RUN apt-get update && \
    apt-get install -y \
        libasound2 \
        ca-certificates \
        wget \
        curl && \
    rm -rf /var/lib/apt/lists/*

# Install libssl1.1 manually (required for Speech SDK, not available in Debian 12)
# Handle both amd64 and arm64 architectures
RUN ARCH=$(dpkg --print-architecture) && \
    if [ "$ARCH" = "amd64" ]; then \
        wget http://archive.ubuntu.com/ubuntu/pool/main/o/openssl/libssl1.1_1.1.1f-1ubuntu2_amd64.deb && \
        dpkg -i libssl1.1_1.1.1f-1ubuntu2_amd64.deb && \
        rm libssl1.1_1.1.1f-1ubuntu2_amd64.deb; \
    elif [ "$ARCH" = "arm64" ]; then \
        wget http://ports.ubuntu.com/pool/main/o/openssl/libssl1.1_1.1.1f-1ubuntu2_arm64.deb && \
        dpkg -i libssl1.1_1.1.1f-1ubuntu2_arm64.deb && \
        rm libssl1.1_1.1.1f-1ubuntu2_arm64.deb; \
    else \
        echo "Unsupported architecture: $ARCH"; \
        exit 1; \
    fi

# Copy published app
COPY --from=build /app/publish .

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "VoiceCode.STTService.dll"]