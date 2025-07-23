#!/bin/bash

# .NET build script for VoiceCode cloud builds

set -euo pipefail

# Source common functions
source /usr/local/bin/common-functions

# Configuration
SOURCE_DIR="/workspace/source"
OUTPUT_DIR="/workspace/output"
CACHE_DIR="/cache/nuget"
BUILD_CONFIG="${1:-Release}"
BUILD_ID="${2:-$(date +%s)}"

log_build_event "BUILD_START" "Starting .NET build (Config: $BUILD_CONFIG, ID: $BUILD_ID)"

# Ensure directories exist
mkdir -p "$OUTPUT_DIR" "$CACHE_DIR" "${WORKSPACE_DIR}/logs"

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

# Detect .NET project structure
detect_dotnet_projects() {
    local solution_files=(*.sln)
    local project_files=(*.csproj *.fsproj *.vbproj)
    
    if [[ -f "${solution_files[0]}" ]]; then
        echo "solution:${solution_files[0]}"
    elif [[ -f "${project_files[0]}" ]]; then
        echo "project:${project_files[0]}"
    else
        # Look in subdirectories
        local found_project=$(find . -name "*.csproj" -o -name "*.fsproj" -o -name "*.vbproj" | head -1)
        if [[ -n "$found_project" ]]; then
            echo "project:$found_project"
        else
            echo "none"
        fi
    fi
}

# Restore packages
restore_packages() {
    local build_target="$1"
    
    log_build_event "PACKAGE_RESTORE" "Restoring NuGet packages"
    
    # Configure NuGet sources (only allow official sources)
    dotnet nuget remove source nuget.org 2>/dev/null || true
    dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
    
    # Restore with timeout and retry
    local restore_attempts=3
    local attempt=1
    
    while [[ $attempt -le $restore_attempts ]]; do
        log_build_event "PACKAGE_RESTORE" "Restore attempt $attempt/$restore_attempts"
        
        if timeout 300 dotnet restore "$build_target" \
            --packages "$CACHE_DIR" \
            --verbosity normal \
            --configfile /dev/null; then
            log_build_event "PACKAGE_RESTORE" "Package restore successful"
            return 0
        fi
        
        ((attempt++))
        sleep 5
    done
    
    handle_build_error 1 "Package restore failed after $restore_attempts attempts"
}

# Build project
build_project() {
    local build_target="$1"
    
    log_build_event "BUILD" "Building .NET project"
    
    # Build with specific configuration
    if ! timeout 600 dotnet build "$build_target" \
        --configuration "$BUILD_CONFIG" \
        --output "$OUTPUT_DIR" \
        --no-restore \
        --verbosity normal \
        --property:TreatWarningsAsErrors=false \
        --property:WarningLevel=1; then
        handle_build_error 1 "Build failed"
    fi
    
    log_build_event "BUILD" "Build completed successfully"
}

# Run tests
run_tests() {
    local build_target="$1"
    
    # Check if test projects exist
    local test_projects=$(find . -name "*Test*.csproj" -o -name "*Tests*.csproj" -o -name "*.Test*.csproj")
    
    if [[ -z "$test_projects" ]]; then
        log_build_event "TEST" "No test projects found, skipping tests"
        return 0
    fi
    
    log_build_event "TEST" "Running unit tests"
    
    # Run tests with timeout
    if ! timeout 300 dotnet test "$build_target" \
        --configuration "$BUILD_CONFIG" \
        --no-build \
        --no-restore \
        --verbosity normal \
        --logger "trx;LogFileName=test-results.trx" \
        --results-directory "${WORKSPACE_DIR}/logs"; then
        log_build_event "TEST" "Some tests failed, but continuing build"
    else
        log_build_event "TEST" "All tests passed"
    fi
}

# Analyze code quality
analyze_code() {
    local build_target="$1"
    
    log_build_event "ANALYSIS" "Running code analysis"
    
    # Run dotnet format (if available)
    if command -v dotnet-format >/dev/null 2>&1; then
        dotnet format "$build_target" --verify-no-changes --verbosity diagnostic || true
    fi
    
    # Run security scan (if available)
    if command -v security-scan >/dev/null 2>&1; then
        security-scan "$build_target" --excl-test=true || true
    fi
    
    log_build_event "ANALYSIS" "Code analysis completed"
}

# Package output
package_output() {
    log_build_event "PACKAGE" "Packaging build output"
    
    # Create deployment package
    if [[ -d "$OUTPUT_DIR" ]]; then
        cd "$OUTPUT_DIR"
        
        # Create archive of build output
        tar -czf "${ARTIFACTS_DIR}/dotnet-build-${BUILD_ID}.tar.gz" .
        
        # Create manifest
        cat > "${ARTIFACTS_DIR}/manifest.json" <<EOF
{
    "build_id": "$BUILD_ID",
    "build_type": "dotnet",
    "build_config": "$BUILD_CONFIG",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "files": $(find . -type f | wc -l),
    "size_bytes": $(du -sb . | cut -f1)
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
    
    # Detect project structure
    local project_info=$(detect_dotnet_projects)
    local project_type=$(echo "$project_info" | cut -d: -f1)
    local project_file=$(echo "$project_info" | cut -d: -f2)
    
    if [[ "$project_type" == "none" ]]; then
        handle_build_error 1 "No .NET project or solution found"
    fi
    
    log_build_event "PROJECT_DETECTED" "Detected $project_type: $project_file"
    
    # Limit file count
    if ! limit_file_count "$SOURCE_DIR" 5000; then
        handle_build_error 1 "Too many files in project"
    fi
    
    # Restore packages
    restore_packages "$project_file"
    
    # Build project
    build_project "$project_file"
    
    # Validate build output
    if ! validate_build_output "$OUTPUT_DIR" "dotnet"; then
        handle_build_error 1 "Build validation failed"
    fi
    
    # Run tests (optional, doesn't fail build)
    run_tests "$project_file"
    
    # Analyze code (optional)
    analyze_code "$project_file"
    
    # Package output
    package_output
    
    # Log performance metrics
    local end_time=$(date +%s)
    local build_duration=$((end_time - start_time))
    log_performance_metric "build_duration_seconds" "$build_duration"
    log_performance_metric "output_size_bytes" "$(du -sb "$OUTPUT_DIR" | cut -f1)"
    
    log_build_event "BUILD_COMPLETE" ".NET build completed successfully in ${build_duration} seconds"
}

# Error handling
trap 'handle_build_error $? "Build script interrupted"' ERR
trap 'cleanup_temp_files' EXIT

# Run main function
main "$@"