#!/bin/bash

# VoiceCode Build Coordinator
# Manages cloud build requests and coordinates with different build agents

set -euo pipefail

# Source common functions
source /usr/local/bin/common-functions

# Configuration
WORKSPACE_DIR="/workspace"
ARTIFACTS_DIR="/artifacts"
CACHE_DIR="/cache"
BUILD_TIMEOUT=1800  # 30 minutes
MAX_CONCURRENT_BUILDS=3

# Logging
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" >&2
}

error() {
    log "ERROR: $*"
    exit 1
}

# Initialize build environment
initialize_build_env() {
    log "Initializing build environment..."
    
    # Clean workspace
    rm -rf ${WORKSPACE_DIR}/*
    rm -rf ${ARTIFACTS_DIR}/*
    
    # Create necessary directories
    mkdir -p ${WORKSPACE_DIR}/{source,output,logs}
    mkdir -p ${ARTIFACTS_DIR}
    mkdir -p ${CACHE_DIR}
    
    # Login to Azure
    if [[ -n "${AZURE_CLIENT_ID:-}" ]]; then
        log "Authenticating with Azure..."
        az login --service-principal \
            --username "${AZURE_CLIENT_ID}" \
            --password "${AZURE_CLIENT_SECRET}" \
            --tenant "${AZURE_TENANT_ID}"
    fi
    
    log "Build environment initialized"
}

# Download source code
download_source() {
    local source_url="$1"
    local source_type="${2:-git}"
    
    log "Downloading source from: $source_url"
    
    case "$source_type" in
        "git")
            git clone "$source_url" "${WORKSPACE_DIR}/source"
            ;;
        "zip")
            wget -O "${WORKSPACE_DIR}/source.zip" "$source_url"
            unzip "${WORKSPACE_DIR}/source.zip" -d "${WORKSPACE_DIR}/source"
            ;;
        "tar")
            wget -O "${WORKSPACE_DIR}/source.tar.gz" "$source_url"
            tar -xzf "${WORKSPACE_DIR}/source.tar.gz" -C "${WORKSPACE_DIR}/source"
            ;;
        *)
            error "Unsupported source type: $source_type"
            ;;
    esac
    
    log "Source downloaded successfully"
}

# Detect project type
detect_project_type() {
    local source_dir="${WORKSPACE_DIR}/source"
    
    if [[ -f "$source_dir/package.json" ]]; then
        echo "nodejs"
    elif [[ -f "$source_dir"/*.csproj ]] || [[ -f "$source_dir"/*.sln ]]; then
        echo "dotnet"
    elif [[ -f "$source_dir/pom.xml" ]]; then
        echo "java"
    elif [[ -f "$source_dir/requirements.txt" ]] || [[ -f "$source_dir/setup.py" ]]; then
        echo "python"
    elif [[ -f "$source_dir/Cargo.toml" ]]; then
        echo "rust"
    elif [[ -f "$source_dir/go.mod" ]]; then
        echo "go"
    else
        echo "unknown"
    fi
}

# Execute build
execute_build() {
    local project_type="$1"
    local build_config="${2:-Release}"
    local build_id="${3:-$(date +%s)}"
    
    log "Starting build for project type: $project_type (ID: $build_id)"
    
    local build_script="/usr/local/bin/build-${project_type}.sh"
    local build_agent_image="voicecode-build-${project_type}:latest"
    
    # Check if specialized build agent exists
    if docker image inspect "$build_agent_image" >/dev/null 2>&1; then
        log "Using specialized build agent: $build_agent_image"
        
        # Run build in specialized container
        docker run --rm \
            -v "${WORKSPACE_DIR}:/workspace" \
            -v "${ARTIFACTS_DIR}:/artifacts" \
            -v "${CACHE_DIR}:/cache" \
            -e "BUILD_CONFIG=$build_config" \
            -e "BUILD_ID=$build_id" \
            --network none \
            --cpus="2" \
            --memory="4g" \
            --user="1000:1000" \
            "$build_agent_image"
    elif [[ -f "$build_script" ]]; then
        log "Using built-in build script: $build_script"
        
        # Execute build script
        timeout "$BUILD_TIMEOUT" "$build_script" "$build_config" "$build_id"
    else
        error "No build method available for project type: $project_type"
    fi
    
    log "Build completed for project type: $project_type"
}

# Upload artifacts
upload_artifacts() {
    local build_id="$1"
    local storage_account="${BUILD_STORAGE_ACCOUNT:-}"
    
    if [[ -z "$storage_account" ]]; then
        log "No storage account configured, skipping artifact upload"
        return 0
    fi
    
    log "Uploading artifacts for build: $build_id"
    
    # Compress artifacts
    local artifact_archive="${ARTIFACTS_DIR}/build-${build_id}.tar.gz"
    tar -czf "$artifact_archive" -C "${WORKSPACE_DIR}/output" .
    
    # Upload to Azure Storage
    az storage blob upload \
        --account-name "$storage_account" \
        --container-name "build-artifacts" \
        --name "builds/${build_id}/artifacts.tar.gz" \
        --file "$artifact_archive" \
        --auth-mode login
    
    # Upload logs
    if [[ -d "${WORKSPACE_DIR}/logs" ]]; then
        local logs_archive="${ARTIFACTS_DIR}/logs-${build_id}.tar.gz"
        tar -czf "$logs_archive" -C "${WORKSPACE_DIR}/logs" .
        
        az storage blob upload \
            --account-name "$storage_account" \
            --container-name "build-artifacts" \
            --name "builds/${build_id}/logs.tar.gz" \
            --file "$logs_archive" \
            --auth-mode login
    fi
    
    log "Artifacts uploaded successfully"
}

# Cleanup build environment
cleanup_build_env() {
    log "Cleaning up build environment..."
    
    # Remove workspace contents
    rm -rf ${WORKSPACE_DIR}/*
    rm -rf ${ARTIFACTS_DIR}/*
    
    # Clean Docker images (keep only base images)
    docker image prune -f
    
    log "Build environment cleaned up"
}

# Handle build request
handle_build_request() {
    local build_request="$1"
    
    # Parse build request JSON
    local source_url=$(echo "$build_request" | jq -r '.source_url')
    local source_type=$(echo "$build_request" | jq -r '.source_type // "git"')
    local build_config=$(echo "$build_request" | jq -r '.build_config // "Release"')
    local build_id=$(echo "$build_request" | jq -r '.build_id // empty')
    
    # Generate build ID if not provided
    if [[ -z "$build_id" ]] || [[ "$build_id" == "null" ]]; then
        build_id="$(date +%s)-$(openssl rand -hex 4)"
    fi
    
    log "Processing build request: $build_id"
    
    # Initialize environment
    initialize_build_env
    
    # Download source
    download_source "$source_url" "$source_type"
    
    # Detect project type
    local project_type=$(detect_project_type)
    log "Detected project type: $project_type"
    
    # Execute build
    execute_build "$project_type" "$build_config" "$build_id"
    
    # Upload artifacts
    upload_artifacts "$build_id"
    
    # Cleanup
    cleanup_build_env
    
    log "Build request completed: $build_id"
    
    # Return build result
    echo "{\"build_id\":\"$build_id\",\"status\":\"completed\",\"project_type\":\"$project_type\"}"
}

# Main coordinator loop
main() {
    log "VoiceCode Build Coordinator starting..."
    
    # Check if running as one-shot build
    if [[ $# -gt 0 ]]; then
        case "$1" in
            "build")
                shift
                local build_request="$1"
                handle_build_request "$build_request"
                ;;
            *)
                error "Unknown command: $1"
                ;;
        esac
        return 0
    fi
    
    # Initialize environment
    initialize_build_env
    
    # Main coordinator loop
    while true; do
        log "Waiting for build requests..."
        
        # Check for build requests from Azure Service Bus, file system, etc.
        # This is a placeholder - in real implementation, would integrate with messaging system
        
        sleep 30
    done
}

# Handle signals
trap cleanup_build_env EXIT
trap 'error "Build interrupted"' INT TERM

# Run main function
main "$@"