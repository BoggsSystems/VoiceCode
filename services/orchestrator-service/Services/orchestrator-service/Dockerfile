FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["services/orchestrator-service/orchestrator-service.csproj", "services/orchestrator-service/"]
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]
RUN dotnet restore "services/orchestrator-service/orchestrator-service.csproj"
COPY . .
WORKDIR "/src/services/orchestrator-service"
RUN dotnet build "orchestrator-service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "orchestrator-service.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "orchestrator-service.dll"]