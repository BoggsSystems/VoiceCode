FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files
COPY ["services/observer-service/ObserverService.csproj", "services/observer-service/"]
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]

# Restore dependencies
RUN dotnet restore "services/observer-service/ObserverService.csproj"

# Copy everything else
COPY . .

# Build
WORKDIR "/src/services/observer-service"
RUN dotnet build "ObserverService.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "ObserverService.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ObserverService.dll"]