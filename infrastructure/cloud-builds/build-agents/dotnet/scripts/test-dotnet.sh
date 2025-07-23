#!/bin/bash

# .NET test script for VoiceCode cloud builds

set -euo pipefail

# Source common functions
source /usr/local/bin/common-functions

# Configuration
SOURCE_DIR="/workspace/source"
OUTPUT_DIR="/workspace/output"
BUILD_CONFIG="${1:-Release}"
BUILD_ID="${2:-$(date +%s)}"

log_build_event "TEST_START" "Starting .NET test execution (Config: $BUILD_CONFIG, ID: $BUILD_ID)"

# Change to source directory
cd "$SOURCE_DIR"

# Find test projects
find_test_projects() {
    find . -name "*.csproj" -exec grep -l "Microsoft.NET.Test.Sdk\|xunit\|nunit\|mstest" {} \; 2>/dev/null || true
}

# Run tests
run_tests() {
    local test_projects=$(find_test_projects)
    
    if [[ -z "$test_projects" ]]; then
        log_build_event "TEST" "No test projects found"
        return 0
    fi
    
    log_build_event "TEST" "Found test projects: $(echo "$test_projects" | tr '\n' ' ')"
    
    # Run tests for each project
    for project in $test_projects; do
        log_build_event "TEST" "Running tests for: $project"
        
        if ! timeout 300 dotnet test "$project" \
            --configuration "$BUILD_CONFIG" \
            --no-build \
            --verbosity normal \
            --logger "trx;LogFileName=test-results-$(basename "$project" .csproj).trx" \
            --results-directory "${WORKSPACE_DIR}/logs"; then
            log_build_event "TEST_FAILED" "Tests failed for: $project"
            return 1
        fi
    done
    
    log_build_event "TEST_COMPLETE" "All tests completed successfully"
    return 0
}

# Main execution
main() {
    if ! check_system_resources; then
        handle_build_error 1 "Insufficient system resources"
    fi
    
    run_tests
    
    log_build_event "TEST_EXECUTION_COMPLETE" ".NET test execution completed"
}

# Error handling
trap 'handle_build_error $? "Test script interrupted"' ERR

# Run main function
main "$@"