# .NET build agent
FROM voicecodebuildsprod.azurecr.io/voicecode-build-base:latest

LABEL description=".NET build agent for VoiceCode cloud builds"

# Switch to root for installation
USER root

# Install .NET SDK
RUN wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y dotnet-sdk-8.0 dotnet-sdk-7.0 dotnet-sdk-6.0 \
    && rm -rf /var/lib/apt/lists/*

# Install additional .NET tools
RUN dotnet tool install --global dotnet-ef \
    && dotnet tool install --global dotnet-format \
    && dotnet tool install --global dotnet-outdated-tool \
    && dotnet tool install --global security-scan

# Install NuGet
RUN curl -o /usr/local/bin/nuget.exe https://dist.nuget.org/win-x86-commandline/latest/nuget.exe

# Install MSBuild (comes with .NET SDK, but ensure it's in PATH)
ENV PATH="${PATH}:/root/.dotnet/tools"

# Copy .NET-specific build scripts
COPY scripts/build-dotnet.sh /usr/local/bin/build-dotnet.sh
COPY scripts/test-dotnet.sh /usr/local/bin/test-dotnet.sh
COPY scripts/analyze-dotnet.sh /usr/local/bin/analyze-dotnet.sh
RUN chmod +x /usr/local/bin/build-dotnet.sh /usr/local/bin/test-dotnet.sh /usr/local/bin/analyze-dotnet.sh

# Create NuGet cache directory
RUN mkdir -p /cache/nuget && chown -R builduser:builduser /cache/nuget

# Configure NuGet cache
ENV NUGET_PACKAGES=/cache/nuget

# Switch back to build user
USER builduser

# Configure .NET environment
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
ENV DOTNET_NOLOGO=1

# Warm up .NET
RUN dotnet --info

# Default command runs the .NET build script
CMD ["/usr/local/bin/build-dotnet.sh"]