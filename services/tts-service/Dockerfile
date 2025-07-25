FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# The app now uses REST API as fallback, so minimal dependencies needed
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ca-certificates \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared project
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]

# Copy service project
COPY ["services/tts-service/VoiceCode.TTSService.csproj", "services/tts-service/"]

# Restore dependencies
RUN dotnet restore "services/tts-service/VoiceCode.TTSService.csproj"

# Copy all files
COPY shared/VoiceCode.Common/ shared/VoiceCode.Common/
COPY services/tts-service/ services/tts-service/

# Build
WORKDIR "/src/services/tts-service"
RUN dotnet build "VoiceCode.TTSService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VoiceCode.TTSService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "VoiceCode.TTSService.dll"]