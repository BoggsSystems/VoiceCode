#!/bin/bash

# Test script for repository cloning in worker containers
# This simulates the startup process to verify repo cloning works

set -e

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Repository Cloning Test${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""

# Test configuration
TEST_REPO="${TEST_REPO:-https://github.com/microsoft/calculator}"  # Public repo for testing
WORKSPACE_DIR="${WORKSPACE_DIR:-/tmp/voicecode-test-workspace}"
SHALLOW_CLONE="${SHALLOW_CLONE:-true}"

# Simulate worker environment variables
export ASSIGNED_REPO="$TEST_REPO"
export GIT_USER_NAME="VoiceCode Test Worker"
export GIT_USER_EMAIL="test-worker@voicecode.dev"
export WORKSPACE_DIR="$WORKSPACE_DIR"
export SHALLOW_CLONE="$SHALLOW_CLONE"

echo -e "${YELLOW}Test Configuration:${NC}"
echo "  - Test Repository: $TEST_REPO"
echo "  - Workspace Directory: $WORKSPACE_DIR"
echo "  - Shallow Clone: $SHALLOW_CLONE"
echo ""

# Clean up previous test
if [ -d "$WORKSPACE_DIR" ]; then
    echo -e "${YELLOW}Cleaning up previous test workspace...${NC}"
    rm -rf "$WORKSPACE_DIR"
fi

# Run the startup script
echo -e "${YELLOW}Running startup script...${NC}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STARTUP_SCRIPT="$SCRIPT_DIR/../services/worker-service/startup.sh"

if [ ! -f "$STARTUP_SCRIPT" ]; then
    echo -e "${RED}Error: startup.sh not found at $STARTUP_SCRIPT${NC}"
    exit 1
fi

# Execute startup script
bash "$STARTUP_SCRIPT"

# Verify results
echo ""
echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Verification Results${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""

if [ -d "$WORKSPACE_DIR/.git" ]; then
    echo -e "${GREEN}✓ Repository cloned successfully${NC}"
    
    cd "$WORKSPACE_DIR"
    
    # Check git configuration
    echo -e "\n${YELLOW}Git Configuration:${NC}"
    echo "  - User Name: $(git config user.name)"
    echo "  - User Email: $(git config user.email)"
    
    # Check repository details
    echo -e "\n${YELLOW}Repository Details:${NC}"
    echo "  - Remote URL: $(git config --get remote.origin.url)"
    echo "  - Current Branch: $(git branch --show-current)"
    echo "  - Commit Count: $(git rev-list --count HEAD)"
    
    # Check file structure
    echo -e "\n${YELLOW}Repository Structure:${NC}"
    echo "  - Total Files: $(find . -type f | grep -v '.git' | wc -l)"
    echo "  - Directories:"
    find . -maxdepth 2 -type d | grep -v '.git' | sort | head -10 | sed 's/^/    /'
    
    # Test Claude Code context awareness
    echo -e "\n${YELLOW}Testing Claude Code Context:${NC}"
    
    # Create a simple CLAUDE.md file
    cat > CLAUDE.md << 'EOF'
# Repository Context for Claude Code

This is a test repository to verify that Claude Code can access and understand the cloned repository context.

## Project Overview
This repository was cloned as part of the VoiceCode worker startup process.

## Key Information
- Worker ID: test-worker
- Repository: Calculator App (Microsoft)
- Purpose: Testing repository cloning functionality

## Available Commands
- `npm run build` - Build the application
- `npm test` - Run tests
EOF
    
    if [ -f "CLAUDE.md" ]; then
        echo -e "${GREEN}✓ Created CLAUDE.md for context${NC}"
    fi
    
    # Simulate Claude Code check
    if command -v claude &> /dev/null; then
        echo -e "\n${YELLOW}Claude Code CLI Status:${NC}"
        claude --version || echo "Claude Code version check failed"
    else
        echo -e "${YELLOW}Note: Claude Code CLI not installed in test environment${NC}"
    fi
    
else
    echo -e "${RED}✗ Repository cloning failed${NC}"
    exit 1
fi

# Performance metrics
echo -e "\n${YELLOW}Performance Metrics:${NC}"
if [ "$SHALLOW_CLONE" == "true" ]; then
    echo "  - Clone Type: Shallow (depth=1)"
    echo "  - Optimized for fast container startup"
else
    echo "  - Clone Type: Full"
    echo "  - Complete git history available"
fi

# Cleanup option
echo -e "\n${YELLOW}Test complete!${NC}"
echo -e "To clean up test workspace, run: ${BLUE}rm -rf $WORKSPACE_DIR${NC}"

echo -e "\n${GREEN}✓ All repository cloning tests passed!${NC}"