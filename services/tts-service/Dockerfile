FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Install dependencies required for Azure Speech SDK
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ca-certificates \
    libasound2 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libc6 \
    libcairo2 \
    libcups2 \
    libdbus-1-3 \
    libexpat1 \
    libfontconfig1 \
    libgcc1 \
    libgconf-2-4 \
    libgdk-pixbuf2.0-0 \
    libglib2.0-0 \
    libgtk-3-0 \
    libnspr4 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libstdc++6 \
    libx11-6 \
    libx11-xcb1 \
    libxcb1 \
    libxcomposite1 \
    libxcursor1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxi6 \
    libxrandr2 \
    libxrender1 \
    libxss1 \
    libxtst6 \
    fonts-liberation \
    libnss3 \
    lsb-release \
    xdg-utils \
    wget \
    libssl3 \
    libgomp1 \
    libatomic1 \
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