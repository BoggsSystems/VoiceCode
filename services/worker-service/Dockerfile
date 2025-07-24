FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install Claude Code CLI
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://storage.googleapis.com/anthropic-public/claude-code/install.sh | sh && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["services/worker-service/VoiceCode.WorkerService.csproj", "services/worker-service/"]
COPY ["services/common/VoiceCode.Common/VoiceCode.Common.csproj", "services/common/VoiceCode.Common/"]
RUN dotnet restore "services/worker-service/VoiceCode.WorkerService.csproj"
COPY . .
WORKDIR "/src/services/worker-service"
RUN dotnet build "VoiceCode.WorkerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VoiceCode.WorkerService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create workspace directory
RUN mkdir -p /workspaces && chmod 777 /workspaces

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "VoiceCode.WorkerService.dll"]