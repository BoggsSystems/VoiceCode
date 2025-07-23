# Base build agent image with common tools
FROM ubuntu:22.04

LABEL maintainer="VoiceCode Team"
LABEL description="Base build agent for VoiceCode cloud builds"

# Avoid prompts from apt
ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=UTC

# Install base packages
RUN apt-get update && apt-get install -y \
    curl \
    wget \
    git \
    unzip \
    zip \
    jq \
    ca-certificates \
    gnupg \
    lsb-release \
    software-properties-common \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

# Install Docker CLI (for building containers)
RUN curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null \
    && apt-get update \
    && apt-get install -y docker-ce-cli \
    && rm -rf /var/lib/apt/lists/*

# Install Azure CLI
RUN curl -sL https://aka.ms/InstallAzureCLIDeb | bash

# Install GitHub CLI
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
    && apt-get update \
    && apt-get install -y gh \
    && rm -rf /var/lib/apt/lists/*

# Create build user
RUN useradd -m -s /bin/bash builduser && \
    echo 'builduser ALL=(ALL) NOPASSWD:ALL' >> /etc/sudoers

# Create directories
RUN mkdir -p /workspace /artifacts /cache && \
    chown -R builduser:builduser /workspace /artifacts /cache

# Install build coordinator script
COPY scripts/build-coordinator.sh /usr/local/bin/build-coordinator
COPY scripts/common-functions.sh /usr/local/bin/common-functions
RUN chmod +x /usr/local/bin/build-coordinator /usr/local/bin/common-functions

# Set working directory
WORKDIR /workspace

# Switch to build user
USER builduser

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["echo", "healthy"]

# Default command
CMD ["/usr/local/bin/build-coordinator"]