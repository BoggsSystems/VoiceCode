#!/bin/bash

# Worker startup script - clones assigned repository
# This runs before the worker service starts

set -e

echo "========================================="
echo "Worker Startup - Repository Setup"
echo "========================================="

# Check if ASSIGNED_REPO is set
if [ -z "$ASSIGNED_REPO" ]; then
    echo "No ASSIGNED_REPO set - running in generic mode"
    exit 0
fi

echo "Assigned Repository: $ASSIGNED_REPO"

# Setup workspace directory
WORKSPACE_DIR="${WORKSPACE_DIR:-/workspace}"
mkdir -p "$WORKSPACE_DIR"
cd "$WORKSPACE_DIR"

# Configure git
if [ ! -z "$GIT_USER_NAME" ]; then
    git config --global user.name "$GIT_USER_NAME"
fi

if [ ! -z "$GIT_USER_EMAIL" ]; then
    git config --global user.email "$GIT_USER_EMAIL"
fi

# Setup GitHub authentication
if [ ! -z "$GITHUB_TOKEN" ]; then
    echo "Configuring GitHub authentication..."
    git config --global credential.helper store
    echo "https://oauth2:${GITHUB_TOKEN}@github.com" > ~/.git-credentials
fi

# Clone the repository
if [ -d ".git" ]; then
    echo "Repository already exists - pulling latest changes..."
    git pull origin main || git pull origin master || echo "Pull failed - continuing with existing code"
else
    echo "Cloning repository..."
    # Use shallow clone for faster startup
    if [ "$SHALLOW_CLONE" = "true" ]; then
        echo "Using shallow clone (depth=1)..."
        git clone --depth 1 "$ASSIGNED_REPO" . || {
            echo "Shallow clone failed, trying full clone..."
            git clone "$ASSIGNED_REPO" .
        }
    else
        git clone "$ASSIGNED_REPO" .
    fi
fi

# Display repository information
echo ""
echo "Repository Setup Complete:"
echo "- Repository: $(git config --get remote.origin.url)"
echo "- Branch: $(git branch --show-current)"
echo "- Last Commit: $(git log -1 --pretty=format:'%h - %s (%cr)')"
echo "- Total Files: $(find . -type f -name '*' | grep -v '.git' | wc -l)"

# Analyze repository for context
echo ""
echo "Analyzing repository structure..."

# Detect project type
if [ -f "package.json" ]; then
    echo "- Detected: Node.js/JavaScript project"
    if [ -f "package-lock.json" ]; then
        echo "- Package Manager: npm"
    elif [ -f "yarn.lock" ]; then
        echo "- Package Manager: yarn"
    fi
fi

if [ -f "requirements.txt" ] || [ -f "setup.py" ] || [ -f "pyproject.toml" ]; then
    echo "- Detected: Python project"
fi

if [ -f "pom.xml" ]; then
    echo "- Detected: Java Maven project"
elif [ -f "build.gradle" ] || [ -f "build.gradle.kts" ]; then
    echo "- Detected: Java Gradle project"
fi

if [ -f "*.csproj" ] || [ -f "*.sln" ]; then
    echo "- Detected: .NET/C# project"
fi

if [ -f "go.mod" ]; then
    echo "- Detected: Go project"
fi

# Check for common directories
[ -d "src" ] && echo "- Source directory: src/"
[ -d "tests" ] || [ -d "test" ] && echo "- Test directory found"
[ -d "docs" ] || [ -d "documentation" ] && echo "- Documentation directory found"
[ -f "README.md" ] && echo "- README.md found"
[ -f ".github/workflows" ] && echo "- GitHub Actions workflows found"

echo ""
echo "Worker is ready with repository context!"
echo "========================================="