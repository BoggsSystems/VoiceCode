#!/bin/bash

# Node.js build script for VoiceCode cloud builds

set -euo pipefail

# Source common functions
source /usr/local/bin/common-functions

# Configuration
SOURCE_DIR="/workspace/source"
OUTPUT_DIR="/workspace/output"
CACHE_DIR="/cache"
BUILD_CONFIG="${1:-production}"
BUILD_ID="${2:-$(date +%s)}"

log_build_event "BUILD_START" "Starting Node.js build (Config: $BUILD_CONFIG, ID: $BUILD_ID)"

# Ensure directories exist
mkdir -p "$OUTPUT_DIR" "${WORKSPACE_DIR}/logs"

# Change to source directory
cd "$SOURCE_DIR"

# Security scan
log_build_event "SECURITY_SCAN" "Scanning for secrets and malicious code"
if ! scan_for_secrets "$SOURCE_DIR"; then
    handle_build_error 1 "Security scan failed: secrets detected"
fi

if ! scan_for_malicious_code "$SOURCE_DIR"; then
    handle_build_error 1 "Security scan failed: malicious code patterns detected"
fi

# Detect package manager and Node.js project type
detect_package_manager() {
    if [[ -f "pnpm-lock.yaml" ]]; then
        echo "pnpm"
    elif [[ -f "yarn.lock" ]]; then
        echo "yarn"
    elif [[ -f "package-lock.json" ]] || [[ -f "package.json" ]]; then
        echo "npm"
    else
        echo "none"
    fi
}

detect_project_type() {
    local package_json="package.json"
    
    if [[ ! -f "$package_json" ]]; then
        echo "unknown"
        return
    fi
    
    # Check for framework indicators
    if jq -e '.dependencies.react or .devDependencies.react' "$package_json" >/dev/null 2>&1; then
        echo "react"
    elif jq -e '.dependencies.vue or .devDependencies.vue' "$package_json" >/dev/null 2>&1; then
        echo "vue"
    elif jq -e '.dependencies."@angular/core" or .devDependencies."@angular/core"' "$package_json" >/dev/null 2>&1; then
        echo "angular"
    elif jq -e '.dependencies.next or .devDependencies.next' "$package_json" >/dev/null 2>&1; then
        echo "nextjs"
    elif jq -e '.dependencies.express or .devDependencies.express' "$package_json" >/dev/null 2>&1; then
        echo "express"
    elif jq -e '.dependencies.typescript or .devDependencies.typescript' "$package_json" >/dev/null 2>&1; then
        echo "typescript"
    else
        echo "nodejs"
    fi
}

# Validate package.json
validate_package_json() {
    local package_json="package.json"
    
    if [[ ! -f "$package_json" ]]; then
        handle_build_error 1 "No package.json found"
    fi
    
    # Validate JSON syntax
    if ! jq empty "$package_json" 2>/dev/null; then
        handle_build_error 1 "Invalid package.json syntax"
    fi
    
    # Check for suspicious packages
    local suspicious_packages=(
        "node-uuid"
        "fs-extra"
        "child_process"
        "shelljs"
    )
    
    for package in "${suspicious_packages[@]}"; do
        if jq -e ".dependencies.\"$package\" or .devDependencies.\"$package\"" "$package_json" >/dev/null 2>&1; then
            log_build_event "SECURITY_WARNING" "Potentially risky package detected: $package"
        fi
    done
    
    log_build_event "VALIDATION" "package.json validation completed"
}

# Set Node.js version
set_node_version() {
    local package_json="package.json"
    
    # Check for .nvmrc file
    if [[ -f ".nvmrc" ]]; then
        local node_version=$(cat .nvmrc | tr -d '\n')
        log_build_event "NODE_VERSION" "Using Node.js version from .nvmrc: $node_version"
        
        # Use n to switch version if available
        if command -v n >/dev/null 2>&1 && n ls | grep -q "$node_version"; then
            n use "$node_version"
        fi
    elif jq -e '.engines.node' "$package_json" >/dev/null 2>&1; then
        local node_version=$(jq -r '.engines.node' "$package_json")
        log_build_event "NODE_VERSION" "Using Node.js version from package.json engines: $node_version"
    else
        log_build_event "NODE_VERSION" "Using default Node.js version: $(node --version)"
    fi
}

# Install dependencies
install_dependencies() {
    local package_manager="$1"
    
    log_build_event "DEPENDENCIES" "Installing dependencies using $package_manager"
    
    case "$package_manager" in
        "npm")
            # Configure npm for security
            npm config set audit-level moderate
            npm config set fund false
            npm config set update-notifier false
            
            # Install with timeout and retry
            local install_attempts=3
            local attempt=1
            
            while [[ $attempt -le $install_attempts ]]; do
                log_build_event "DEPENDENCIES" "Install attempt $attempt/$install_attempts"
                
                if timeout 600 npm ci --only=production --no-audit --no-fund; then
                    log_build_event "DEPENDENCIES" "Dependencies installed successfully"
                    return 0
                fi
                
                ((attempt++))
                sleep 5
            done
            
            handle_build_error 1 "Dependency installation failed after $install_attempts attempts"
            ;;
        "yarn")
            # Configure yarn
            yarn config set network-timeout 300000
            
            if timeout 600 yarn install --frozen-lockfile --production; then
                log_build_event "DEPENDENCIES" "Dependencies installed successfully"
            else
                handle_build_error 1 "Yarn dependency installation failed"
            fi
            ;;
        "pnpm")
            if timeout 600 pnpm install --frozen-lockfile --prod; then
                log_build_event "DEPENDENCIES" "Dependencies installed successfully"
            else
                handle_build_error 1 "PNPM dependency installation failed"
            fi
            ;;
        *)
            handle_build_error 1 "Unknown package manager: $package_manager"
            ;;
    esac
}

# Build project
build_project() {
    local package_manager="$1"
    local project_type="$2"
    
    log_build_event "BUILD" "Building $project_type project"
    
    # Determine build script
    local build_script="build"
    if jq -e '.scripts.build' package.json >/dev/null 2>&1; then
        build_script="build"
    elif jq -e '.scripts.compile' package.json >/dev/null 2>&1; then
        build_script="compile"
    elif jq -e '.scripts.dist' package.json >/dev/null 2>&1; then
        build_script="dist"
    else
        log_build_event "BUILD" "No build script found, skipping build step"
        
        # For simple projects, just copy source to output
        cp -r . "$OUTPUT_DIR/"
        return 0
    fi
    
    # Set build environment
    export NODE_ENV="$BUILD_CONFIG"
    
    # Run build with timeout
    case "$package_manager" in
        "npm")
            if ! timeout 900 npm run "$build_script"; then
                handle_build_error 1 "NPM build failed"
            fi
            ;;
        "yarn")
            if ! timeout 900 yarn "$build_script"; then
                handle_build_error 1 "Yarn build failed"
            fi
            ;;
        "pnpm")
            if ! timeout 900 pnpm run "$build_script"; then
                handle_build_error 1 "PNPM build failed"
            fi
            ;;
    esac
    
    # Copy build output
    copy_build_output "$project_type"
    
    log_build_event "BUILD" "Build completed successfully"
}

# Copy build output to output directory
copy_build_output() {
    local project_type="$1"
    
    # Determine output directory based on project type
    local source_output_dir=""
    
    case "$project_type" in
        "react"|"vue"|"angular")
            # Check common build output directories
            for dir in "build" "dist" "public"; do
                if [[ -d "$dir" ]]; then
                    source_output_dir="$dir"
                    break
                fi
            done
            ;;
        "nextjs")
            source_output_dir=".next"
            ;;
        *)
            # For other projects, copy everything except node_modules
            rsync -av --exclude=node_modules --exclude=.git . "$OUTPUT_DIR/"
            return 0
            ;;
    esac
    
    if [[ -n "$source_output_dir" ]] && [[ -d "$source_output_dir" ]]; then
        cp -r "$source_output_dir"/* "$OUTPUT_DIR/"
    else
        # Fallback: copy everything except node_modules
        rsync -av --exclude=node_modules --exclude=.git . "$OUTPUT_DIR/"
    fi
}

# Run tests
run_tests() {
    local package_manager="$1"
    
    # Check if test script exists
    if ! jq -e '.scripts.test' package.json >/dev/null 2>&1; then
        log_build_event "TEST" "No test script found, skipping tests"
        return 0
    fi
    
    log_build_event "TEST" "Running tests"
    
    # Run tests with timeout (non-blocking)
    case "$package_manager" in
        "npm")
            if timeout 300 npm test; then
                log_build_event "TEST" "All tests passed"
            else
                log_build_event "TEST" "Some tests failed, but continuing build"
            fi
            ;;
        "yarn")
            if timeout 300 yarn test; then
                log_build_event "TEST" "All tests passed"
            else
                log_build_event "TEST" "Some tests failed, but continuing build"
            fi
            ;;
        "pnpm")
            if timeout 300 pnpm test; then
                log_build_event "TEST" "All tests passed"
            else
                log_build_event "TEST" "Some tests failed, but continuing build"
            fi
            ;;
    esac
}

# Package output
package_output() {
    log_build_event "PACKAGE" "Packaging build output"
    
    if [[ -d "$OUTPUT_DIR" ]] && [[ -n "$(ls -A "$OUTPUT_DIR")" ]]; then
        cd "$OUTPUT_DIR"
        
        # Create archive of build output
        tar -czf "${ARTIFACTS_DIR}/nodejs-build-${BUILD_ID}.tar.gz" .
        
        # Create manifest
        cat > "${ARTIFACTS_DIR}/manifest.json" <<EOF
{
    "build_id": "$BUILD_ID",
    "build_type": "nodejs",
    "build_config": "$BUILD_CONFIG",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "files": $(find . -type f | wc -l),
    "size_bytes": $(du -sb . | cut -f1),
    "node_version": "$(node --version)",
    "npm_version": "$(npm --version)"
}
EOF
        
        log_build_event "PACKAGE" "Package created successfully"
    else
        handle_build_error 1 "No build output found"
    fi
}

# Main build process
main() {
    local start_time=$(date +%s)
    
    # Check system resources
    if ! check_system_resources; then
        handle_build_error 1 "Insufficient system resources"
    fi
    
    # Validate package.json
    validate_package_json
    
    # Detect package manager and project type
    local package_manager=$(detect_package_manager)
    local project_type=$(detect_project_type)
    
    if [[ "$package_manager" == "none" ]]; then
        handle_build_error 1 "No package manager configuration found"
    fi
    
    log_build_event "PROJECT_DETECTED" "Detected $project_type project using $package_manager"
    
    # Limit file count
    if ! limit_file_count "$SOURCE_DIR" 10000; then
        handle_build_error 1 "Too many files in project"
    fi
    
    # Set Node.js version
    set_node_version
    
    # Install dependencies
    install_dependencies "$package_manager"
    
    # Build project
    build_project "$package_manager" "$project_type"
    
    # Validate build output
    if ! validate_build_output "$OUTPUT_DIR" "nodejs"; then
        handle_build_error 1 "Build validation failed"
    fi
    
    # Run tests (optional, doesn't fail build)
    run_tests "$package_manager"
    
    # Package output
    package_output
    
    # Log performance metrics
    local end_time=$(date +%s)
    local build_duration=$((end_time - start_time))
    log_performance_metric "build_duration_seconds" "$build_duration"
    log_performance_metric "output_size_bytes" "$(du -sb "$OUTPUT_DIR" | cut -f1)"
    
    log_build_event "BUILD_COMPLETE" "Node.js build completed successfully in ${build_duration} seconds"
}

# Error handling
trap 'handle_build_error $? "Build script interrupted"' ERR
trap 'cleanup_temp_files' EXIT

# Run main function
main "$@"