FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

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

# Install dependencies for Azure Speech SDK
RUN apt-get update && apt-get install -y \
    libasound2 \
    libssl-dev \
    ca-certificates \
    libc6 \
    libgcc1 \
    libgssapi-krb5-2 \
    libicu72 \
    liblttng-ust1 \
    libssl3 \
    libstdc++6 \
    zlib1g \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "VoiceCode.TTSService.dll"]