FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared project
COPY ["shared/VoiceCode.Common/VoiceCode.Common.csproj", "shared/VoiceCode.Common/"]

# Copy service project
COPY ["services/dispatcher-service/VoiceCode.DispatcherService.csproj", "services/dispatcher-service/"]

# Restore dependencies
RUN dotnet restore "services/dispatcher-service/VoiceCode.DispatcherService.csproj"

# Copy all files
COPY shared/VoiceCode.Common/ shared/VoiceCode.Common/
COPY services/dispatcher-service/ services/dispatcher-service/

# Build
WORKDIR "/src/services/dispatcher-service"
RUN dotnet build "VoiceCode.DispatcherService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VoiceCode.DispatcherService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "VoiceCode.DispatcherService.dll"]