# Node.js build agent
FROM voicecode-build-base:latest

LABEL description="Node.js build agent for VoiceCode cloud builds"

# Switch to root for installation
USER root

# Install Node.js 20 LTS, 18 LTS, and 16 LTS
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install additional Node versions using n
RUN npm install -g n \
    && n 18.19.0 \
    && n 16.20.2

# Install global tools
RUN npm install -g \
    yarn \
    pnpm \
    typescript \
    @typescript-eslint/eslint-plugin \
    @typescript-eslint/parser \
    prettier \
    eslint \
    webpack-cli \
    @angular/cli \
    @vue/cli \
    create-react-app \
    next \
    vite

# Copy Node.js-specific build scripts
COPY scripts/build-nodejs.sh /usr/local/bin/build-nodejs.sh
COPY scripts/test-nodejs.sh /usr/local/bin/test-nodejs.sh
COPY scripts/analyze-nodejs.sh /usr/local/bin/analyze-nodejs.sh
RUN chmod +x /usr/local/bin/build-nodejs.sh /usr/local/bin/test-nodejs.sh /usr/local/bin/analyze-nodejs.sh

# Create npm cache directory
RUN mkdir -p /cache/npm /cache/yarn /cache/pnpm \
    && chown -R builduser:builduser /cache/npm /cache/yarn /cache/pnpm

# Configure package manager caches
ENV NPM_CONFIG_CACHE=/cache/npm
ENV YARN_CACHE_FOLDER=/cache/yarn
ENV PNPM_HOME=/cache/pnpm

# Switch back to build user
USER builduser

# Configure Node.js environment
ENV NODE_ENV=production
ENV NPM_CONFIG_AUDIT=false
ENV NPM_CONFIG_FUND=false
ENV NPM_CONFIG_UPDATE_NOTIFIER=false

# Warm up package managers
RUN npm --version && yarn --version && pnpm --version

# Default command runs the Node.js build script
CMD ["/usr/local/bin/build-nodejs.sh"]