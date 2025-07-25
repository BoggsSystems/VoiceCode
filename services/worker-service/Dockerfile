FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install Claude Code CLI and git
RUN apt-get update && \
    apt-get install -y curl git && \
    curl -fsSL https://storage.googleapis.com/anthropic-public/claude-code/install.sh | sh && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["services/worker-service/VoiceCode.WorkerService.csproj", "services/worker-service/"]
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]
RUN dotnet restore "services/worker-service/VoiceCode.WorkerService.csproj"
COPY . .
WORKDIR "/src/services/worker-service"
RUN dotnet build "VoiceCode.WorkerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VoiceCode.WorkerService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy startup script
COPY services/worker-service/startup.sh /startup.sh
RUN chmod +x /startup.sh

# Create workspace directory
RUN mkdir -p /workspace && chmod 777 /workspace
RUN mkdir -p /workspaces && chmod 777 /workspaces

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV WORKSPACE_DIR=/workspace
ENV SHALLOW_CLONE=true

# Create entrypoint script that runs startup then the app
RUN echo '#!/bin/bash\n\
/startup.sh\n\
exec dotnet VoiceCode.WorkerService.dll' > /entrypoint.sh && \
chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]