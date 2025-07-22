# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["VoiceCode.sln", "."]
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]
COPY ["services/router-service/VoiceCode.RouterService.csproj", "services/router-service/"]

# Restore dependencies
RUN dotnet restore "services/router-service/VoiceCode.RouterService.csproj"

# Copy source code
COPY ["shared/VoiceCode.Common/", "shared/VoiceCode.Common/"]
COPY ["services/router-service/", "services/router-service/"]

# Build and publish
WORKDIR "/src/services/router-service"
RUN dotnet build "VoiceCode.RouterService.csproj" -c Release -o /app/build
RUN dotnet publish "VoiceCode.RouterService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create directory for ML models
RUN mkdir -p /app/Models

# Copy published app
COPY --from=build /app/publish .

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "VoiceCode.RouterService.dll"]