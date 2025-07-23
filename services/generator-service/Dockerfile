# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["VoiceCode.sln", "."]
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]
COPY ["services/generator-service/VoiceCode.GeneratorService.csproj", "services/generator-service/"]

# Restore dependencies
RUN dotnet restore "services/generator-service/VoiceCode.GeneratorService.csproj"

# Copy source code
COPY ["shared/VoiceCode.Common/", "shared/VoiceCode.Common/"]
COPY ["services/generator-service/", "services/generator-service/"]

# Build and publish
WORKDIR "/src/services/generator-service"
RUN dotnet build "VoiceCode.GeneratorService.csproj" -c Release -o /app/build
RUN dotnet publish "VoiceCode.GeneratorService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create directories
RUN mkdir -p /app/Templates

# Copy published app
COPY --from=build /app/publish .

# Copy templates if any exist
# Note: Templates directory is currently empty, so we skip this step

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "VoiceCode.GeneratorService.dll"]