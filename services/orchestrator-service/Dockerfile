FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["services/orchestrator-service/VoiceCode.OrchestratorService.csproj", "services/orchestrator-service/"]
COPY ["services/common/VoiceCode.Common/VoiceCode.Common.csproj", "services/common/VoiceCode.Common/"]
RUN dotnet restore "services/orchestrator-service/VoiceCode.OrchestratorService.csproj"
COPY . .
WORKDIR "/src/services/orchestrator-service"
RUN dotnet build "VoiceCode.OrchestratorService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VoiceCode.OrchestratorService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "VoiceCode.OrchestratorService.dll"]