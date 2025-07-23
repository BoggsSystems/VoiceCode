#!/bin/bash

# VoiceCode Build Manager
# Manages cloud builds and coordinates with GitHub Actions

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_FILE="${SCRIPT_DIR}/../config/build-config.json"
LOG_DIR="${SCRIPT_DIR}/../logs"
GITHUB_REPO="${GITHUB_REPOSITORY:-}"
GITHUB_TOKEN="${GITHUB_TOKEN:-}"

# Ensure log directory exists
mkdir -p "$LOG_DIR"

# Logging
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "${LOG_DIR}/build-manager.log"
}

error() {
    log "ERROR: $*"
    exit 1
}

# Load configuration
load_config() {
    if [[ -f "$CONFIG_FILE" ]]; then
        CONFIG=$(cat "$CONFIG_FILE")
        log "Configuration loaded from $CONFIG_FILE"
    else
        log "No configuration file found, using defaults"
        CONFIG='{
            "max_concurrent_builds": 5,
            "default_timeout_minutes": 30,
            "allowed_source_types": ["git", "zip", "tar"],
            "allowed_project_types": ["dotnet", "nodejs", "python", "java", "rust", "go"],
            "security_scan_enabled": true,
            "artifact_retention_days": 7
        }'
    fi
}

# Validate build request
validate_build_request() {
    local request="$1"
    
    # Parse request JSON
    local source_url=$(echo "$request" | jq -r '.source_url // empty')
    local source_type=$(echo "$request" | jq -r '.source_type // "git"')
    local project_type=$(echo "$request" | jq -r '.project_type // "auto-detect"')
    local user_id=$(echo "$request" | jq -r '.user_id // empty')
    
    # Validate required fields
    if [[ -z "$source_url" ]]; then
        echo "Missing required field: source_url"
        return 1
    fi
    
    if [[ -z "$user_id" ]]; then
        echo "Missing required field: user_id"
        return 1
    fi
    
    # Validate source URL format
    if [[ ! "$source_url" =~ ^https?:// ]]; then
        echo "Invalid source URL format: must be http or https"
        return 1
    fi
    
    # Validate source type
    local allowed_source_types=$(echo "$CONFIG" | jq -r '.allowed_source_types[]')
    if ! echo "$allowed_source_types" | grep -q "^${source_type}$"; then
        echo "Invalid source type: $source_type"
        return 1
    fi
    
    # Validate project type
    if [[ "$project_type" != "auto-detect" ]]; then
        local allowed_project_types=$(echo "$CONFIG" | jq -r '.allowed_project_types[]')
        if ! echo "$allowed_project_types" | grep -q "^${project_type}$"; then
            echo "Invalid project type: $project_type"
            return 1
        fi
    fi
    
    log "Build request validation passed"
    return 0
}

# Check build capacity
check_build_capacity() {
    local max_builds=$(echo "$CONFIG" | jq -r '.max_concurrent_builds')
    
    # Get current running builds (this would query the actual build system)
    local current_builds=0  # Placeholder - would check actual running builds
    
    if [[ $current_builds -ge $max_builds ]]; then
        echo "Build capacity exceeded: $current_builds/$max_builds"
        return 1
    fi
    
    log "Build capacity available: $current_builds/$max_builds"
    return 0
}

# Trigger GitHub Actions workflow
trigger_github_workflow() {
    local build_request="$1"
    
    if [[ -z "$GITHUB_REPO" ]] || [[ -z "$GITHUB_TOKEN" ]]; then
        error "GitHub repository or token not configured"
    fi
    
    # Parse request
    local source_url=$(echo "$build_request" | jq -r '.source_url')
    local source_type=$(echo "$build_request" | jq -r '.source_type // "git"')
    local project_type=$(echo "$build_request" | jq -r '.project_type // "auto-detect"')
    local build_config=$(echo "$build_request" | jq -r '.build_config // "Release"')
    local build_timeout=$(echo "$build_request" | jq -r '.build_timeout // 30')
    local user_id=$(echo "$build_request" | jq -r '.user_id')
    
    log "Triggering GitHub Actions workflow for user: $user_id"
    
    # Create workflow dispatch payload
    local workflow_payload=$(jq -n \
        --arg source_url "$source_url" \
        --arg source_type "$source_type" \
        --arg project_type "$project_type" \
        --arg build_config "$build_config" \
        --arg build_timeout "$build_timeout" \
        --arg user_id "$user_id" \
        '{
            ref: "main",
            inputs: {
                source_url: $source_url,
                source_type: $source_type,
                project_type: $project_type,
                build_config: $build_config,
                build_timeout: $build_timeout,
                user_id: $user_id
            }
        }')
    
    # Trigger workflow
    local response=$(curl -s -w "\n%{http_code}" \
        -X POST \
        -H "Authorization: token $GITHUB_TOKEN" \
        -H "Accept: application/vnd.github.v3+json" \
        -d "$workflow_payload" \
        "https://api.github.com/repos/$GITHUB_REPO/actions/workflows/user-build.yml/dispatches")
    
    local http_code=$(echo "$response" | tail -n1)
    local response_body=$(echo "$response" | head -n -1)
    
    if [[ "$http_code" == "204" ]]; then
        log "GitHub Actions workflow triggered successfully"
        return 0
    else
        error "Failed to trigger GitHub Actions workflow: HTTP $http_code - $response_body"
    fi
}

# Submit build request
submit_build() {
    local build_request="$1"
    
    log "Processing build request..."
    
    # Validate request
    if ! validate_build_request "$build_request"; then
        error "Build request validation failed"
    fi
    
    # Check capacity
    if ! check_build_capacity; then
        error "No build capacity available"
    fi
    
    # Trigger build
    if trigger_github_workflow "$build_request"; then
        log "Build request submitted successfully"
        
        # Generate response
        local build_id="build-$(date +%s)-$(openssl rand -hex 4)"
        local user_id=$(echo "$build_request" | jq -r '.user_id')
        
        jq -n \
            --arg build_id "$build_id" \
            --arg user_id "$user_id" \
            --arg status "submitted" \
            --arg timestamp "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
            '{
                build_id: $build_id,
                user_id: $user_id,
                status: $status,
                message: "Build request submitted successfully",
                timestamp: $timestamp
            }'
    else
        error "Failed to submit build request"
    fi
}

# Get build status
get_build_status() {
    local build_id="$1"
    
    # This would query the actual build system for status
    # For now, return a placeholder response
    jq -n \
        --arg build_id "$build_id" \
        --arg status "running" \
        --arg timestamp "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        '{
            build_id: $build_id,
            status: $status,
            message: "Build in progress",
            timestamp: $timestamp,
            progress: 50
        }'
}

# List user builds
list_user_builds() {
    local user_id="$1"
    local limit="${2:-10}"
    
    # This would query the actual build database
    # For now, return a placeholder response
    jq -n \
        --arg user_id "$user_id" \
        --argjson limit "$limit" \
        '{
            user_id: $user_id,
            builds: [
                {
                    build_id: "build-example-1",
                    status: "completed",
                    project_type: "dotnet",
                    timestamp: "2024-01-15T10:30:00Z"
                },
                {
                    build_id: "build-example-2",
                    status: "running",
                    project_type: "nodejs",
                    timestamp: "2024-01-15T11:00:00Z"
                }
            ],
            total: 2,
            limit: $limit
        }'
}

# Cancel build
cancel_build() {
    local build_id="$1"
    local user_id="$2"
    
    log "Cancelling build: $build_id for user: $user_id"
    
    # This would cancel the actual build
    # For now, return success
    jq -n \
        --arg build_id "$build_id" \
        --arg user_id "$user_id" \
        --arg status "cancelled" \
        --arg timestamp "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        '{
            build_id: $build_id,
            user_id: $user_id,
            status: $status,
            message: "Build cancelled successfully",
            timestamp: $timestamp
        }'
}

# Main function
main() {
    local command="${1:-help}"
    
    # Load configuration
    load_config
    
    case "$command" in
        "submit")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 submit <build_request_json>"
            fi
            submit_build "$2"
            ;;
        "status")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 status <build_id>"
            fi
            get_build_status "$2"
            ;;
        "list")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 list <user_id> [limit]"
            fi
            list_user_builds "$2" "${3:-10}"
            ;;
        "cancel")
            if [[ $# -lt 3 ]]; then
                error "Usage: $0 cancel <build_id> <user_id>"
            fi
            cancel_build "$2" "$3"
            ;;
        "validate")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 validate <build_request_json>"
            fi
            if validate_build_request "$2"; then
                echo '{"valid": true, "message": "Request is valid"}'
            else
                echo '{"valid": false, "message": "Request validation failed"}'
            fi
            ;;
        "help"|*)
            cat << EOF
VoiceCode Build Manager

Usage: $0 <command> [options]

Commands:
  submit <json>         Submit a new build request
  status <build_id>     Get build status
  list <user_id> [limit] List user builds
  cancel <build_id> <user_id> Cancel a build
  validate <json>       Validate build request
  help                  Show this help

Build Request JSON Format:
{
  "source_url": "https://github.com/user/repo.git",
  "source_type": "git",
  "project_type": "dotnet",
  "build_config": "Release",
  "build_timeout": 30,
  "user_id": "user123"
}

Examples:
  $0 submit '{"source_url":"https://github.com/user/repo.git","user_id":"user123"}'
  $0 status build-1234567890-abcd
  $0 list user123 5
  $0 cancel build-1234567890-abcd user123
EOF
            ;;
    esac
}

# Run main function with all arguments
main "$@"