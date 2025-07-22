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
        libatk1.0-0 \
        libcairo2 \
        libgtk-3-0 \
        libpango-1.0-0 \
        libgdk-pixbuf2.0-0 \
        libx11-xcb1 \
        libxcb-dri3-0 \
        libxss1 \
        libgconf-2-4 \
        libxcomposite1 \
        libxcursor1 \
        libxdamage1 \
        libxi6 \
        libxtst6 \
        libappindicator3-1 \
        libnss3 \
        libnspr4 \
        libsecret-1-0 \
        ca-certificates \
        fonts-liberation \
        libasound2-plugins \
        libatk-bridge2.0-0 \
        libatspi2.0-0 \
        libdrm2 \
        libgbm1 \
        libxkbcommon0 \
        libxrandr2 \
        xdg-utils \
        wget && \
    rm -rf /var/lib/apt/lists/*

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