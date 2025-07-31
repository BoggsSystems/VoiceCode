# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared projects first
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]

# Copy orchestrator service project
COPY ["services/orchestrator-service/Services/orchestrator-service/orchestrator-service.csproj", "services/orchestrator-service/Services/orchestrator-service/"]

# Restore dependencies
RUN dotnet restore "services/orchestrator-service/Services/orchestrator-service/orchestrator-service.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/services/orchestrator-service/Services/orchestrator-service"
RUN dotnet build "orchestrator-service.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "orchestrator-service.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Copy published application
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Add health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

ENTRYPOINT ["dotnet", "orchestrator-service.dll"]