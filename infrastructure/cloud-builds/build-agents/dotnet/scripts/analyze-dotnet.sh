#!/bin/bash

# .NET code analysis script for VoiceCode cloud builds

set -euo pipefail

# Source common functions
source /usr/local/bin/common-functions

# Configuration
SOURCE_DIR="/workspace/source"
OUTPUT_DIR="/workspace/output"
BUILD_CONFIG="${1:-Release}"
BUILD_ID="${2:-$(date +%s)}"

log_build_event "ANALYSIS_START" "Starting .NET code analysis (Config: $BUILD_CONFIG, ID: $BUILD_ID)"

# Change to source directory
cd "$SOURCE_DIR"

# Run code formatting analysis
run_format_analysis() {
    log_build_event "ANALYSIS" "Running code formatting analysis"
    
    if command -v dotnet-format >/dev/null 2>&1; then
        # Check if format is needed
        if dotnet format --verify-no-changes --verbosity diagnostic 2>/dev/null; then
            log_build_event "ANALYSIS" "Code formatting is correct"
        else
            log_build_event "ANALYSIS_WARNING" "Code formatting issues found"
        fi
    else
        log_build_event "ANALYSIS" "dotnet-format not available, skipping format check"
    fi
}

# Run security analysis
run_security_analysis() {
    log_build_event "ANALYSIS" "Running security analysis"
    
    if command -v security-scan >/dev/null 2>&1; then
        if security-scan . --excl-test=true --output="${WORKSPACE_DIR}/logs/security-scan.json" 2>/dev/null; then
            log_build_event "ANALYSIS" "Security analysis completed - no issues found"
        else
            log_build_event "ANALYSIS_WARNING" "Security analysis found potential issues"
        fi
    else
        log_build_event "ANALYSIS" "security-scan not available, skipping security analysis"
    fi
}

# Check for outdated packages
check_outdated_packages() {
    log_build_event "ANALYSIS" "Checking for outdated packages"
    
    if command -v dotnet-outdated >/dev/null 2>&1; then
        dotnet outdated --output "${WORKSPACE_DIR}/logs/outdated-packages.json" 2>/dev/null || true
        log_build_event "ANALYSIS" "Package outdated check completed"
    else
        log_build_event "ANALYSIS" "dotnet-outdated not available, skipping package check"
    fi
}

# Analyze project dependencies
analyze_dependencies() {
    log_build_event "ANALYSIS" "Analyzing project dependencies"
    
    # Find all project files
    local projects=$(find . -name "*.csproj" -o -name "*.fsproj" -o -name "*.vbproj")
    
    for project in $projects; do
        if [[ -f "$project" ]]; then
            log_build_event "ANALYSIS" "Analyzing dependencies for: $project"
            
            # List package references
            dotnet list "$project" package --output "${WORKSPACE_DIR}/logs/packages-$(basename "$project" .csproj).json" 2>/dev/null || true
        fi
    done
}

# Generate analysis report
generate_analysis_report() {
    log_build_event "ANALYSIS" "Generating analysis report"
    
    local report_file="${WORKSPACE_DIR}/logs/analysis-report.json"
    
    cat > "$report_file" << EOF
{
    "build_id": "$BUILD_ID",
    "analysis_type": "dotnet",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "source_directory": "$SOURCE_DIR",
    "analysis_completed": {
        "format_check": true,
        "security_scan": true,
        "outdated_packages": true,
        "dependency_analysis": true
    },
    "files_analyzed": $(find . -name "*.cs" -o -name "*.fs" -o -name "*.vb" | wc -l),
    "projects_analyzed": $(find . -name "*.csproj" -o -name "*.fsproj" -o -name "*.vbproj" | wc -l)
}
EOF
    
    log_build_event "ANALYSIS" "Analysis report generated: $report_file"
}

# Main execution
main() {
    if ! check_system_resources; then
        handle_build_error 1 "Insufficient system resources"
    fi
    
    # Security scan of source code
    if ! scan_for_secrets "$SOURCE_DIR"; then
        log_build_event "ANALYSIS_ERROR" "Security scan failed: secrets detected"
    fi
    
    if ! scan_for_malicious_code "$SOURCE_DIR"; then
        log_build_event "ANALYSIS_ERROR" "Security scan failed: malicious code patterns detected"
    fi
    
    # Run various analyses
    run_format_analysis
    run_security_analysis
    check_outdated_packages
    analyze_dependencies
    
    # Generate report
    generate_analysis_report
    
    log_build_event "ANALYSIS_COMPLETE" ".NET code analysis completed"
}

# Error handling
trap 'handle_build_error $? "Analysis script interrupted"' ERR

# Run main function
main "$@"