FROM node:20-alpine AS builder

WORKDIR /app

# Copy package files
COPY package*.json ./
COPY tsconfig.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY src ./src

# Build TypeScript
RUN npm run build

# Production stage
FROM node:20-alpine

WORKDIR /app

# Install production dependencies
COPY package*.json ./
RUN npm ci --production

# Copy built application
COPY --from=builder /app/dist ./dist

# Create project directory for mounted code
RUN mkdir -p /project

# Create non-root user
RUN addgroup -g 1001 -S nodejs
RUN adduser -S nodejs -u 1001

# Set ownership (app directory only, /project will be mounted)
RUN chown -R nodejs:nodejs /app

USER nodejs

EXPOSE 3000

# Environment variables
ENV NODE_ENV=production
ENV PORT=3000
ENV PROJECT_ROOT=/project

CMD ["node", "dist/index.js"]