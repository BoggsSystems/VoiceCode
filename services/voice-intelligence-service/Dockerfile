FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["services/voice-intelligence-service/VoiceCode.VoiceIntelligenceService.csproj", "services/voice-intelligence-service/"]
RUN dotnet restore "services/voice-intelligence-service/VoiceCode.VoiceIntelligenceService.csproj"
COPY . .
WORKDIR "/src/services/voice-intelligence-service"
RUN dotnet build "VoiceCode.VoiceIntelligenceService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VoiceCode.VoiceIntelligenceService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "VoiceCode.VoiceIntelligenceService.dll"]