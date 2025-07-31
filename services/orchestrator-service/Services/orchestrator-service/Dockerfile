FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["orchestrator-service.csproj", "./"]
RUN dotnet restore "orchestrator-service.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "orchestrator-service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "orchestrator-service.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
EXPOSE 443

COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "orchestrator-service.dll"]