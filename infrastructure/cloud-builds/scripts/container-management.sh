#!/bin/bash

# Azure Container Instance Management for VoiceCode Builds
# Manages creation, monitoring, and cleanup of build containers

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESOURCE_GROUP="${AZURE_RESOURCE_GROUP:-voicecode-rg}"
REGISTRY_NAME="${AZURE_CONTAINER_REGISTRY:-voicecodebuilds}"
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-}"
BUILD_STORAGE="${BUILD_STORAGE_ACCOUNT:-}"

# Logging
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" >&2
}

error() {
    log "ERROR: $*"
    exit 1
}

# Check prerequisites
check_prerequisites() {
    # Check Azure CLI
    if ! command -v az >/dev/null 2>&1; then
        error "Azure CLI not found. Please install Azure CLI."
    fi
    
    # Check login
    if ! az account show >/dev/null 2>&1; then
        error "Not logged in to Azure. Please run 'az login'."
    fi
    
    # Check subscription
    if [[ -n "$SUBSCRIPTION_ID" ]]; then
        az account set --subscription "$SUBSCRIPTION_ID"
    fi
    
    log "Prerequisites check passed"
}

# Create build container instance
create_build_container() {
    local build_id="$1"
    local project_type="${2:-base}"
    local source_url="$3"
    local build_config="${4:-Release}"
    local user_id="$5"
    
    log "Creating build container for build ID: $build_id"
    
    # Determine container image
    local image_name="voicecode-build-${project_type}"
    if [[ "$project_type" == "auto" ]]; then
        image_name="voicecode-build-base"
    fi
    
    local full_image="${REGISTRY_NAME}.azurecr.io/${image_name}:latest"
    
    # Container name (must be lowercase and alphanumeric)
    local container_name=$(echo "build-${build_id}" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9-]//g')
    
    log "Creating container: $container_name with image: $full_image"
    
    # Create container instance
    az container create \
        --resource-group "$RESOURCE_GROUP" \
        --name "$container_name" \
        --image "$full_image" \
        --cpu 2 \
        --memory 4 \
        --restart-policy Never \
        --location "East US" \
        --os-type Linux \
        --ip-address None \
        --environment-variables \
            BUILD_ID="$build_id" \
            PROJECT_TYPE="$project_type" \
            SOURCE_URL="$source_url" \
            BUILD_CONFIG="$build_config" \
            USER_ID="$user_id" \
            BUILD_STORAGE="$BUILD_STORAGE" \
            AZURE_SUBSCRIPTION_ID="$SUBSCRIPTION_ID" \
            RESOURCE_GROUP_NAME="$RESOURCE_GROUP" \
        --secure-environment-variables \
            AZURE_CLIENT_ID="$AZURE_CLIENT_ID" \
            AZURE_CLIENT_SECRET="$AZURE_CLIENT_SECRET" \
            AZURE_TENANT_ID="$AZURE_TENANT_ID" \
        --assign-identity \
        --output table
    
    if [[ $? -eq 0 ]]; then
        log "Container $container_name created successfully"
        echo "$container_name"
    else
        error "Failed to create container $container_name"
    fi
}

# Monitor container status
monitor_container() {
    local container_name="$1"
    local timeout_minutes="${2:-30}"
    local check_interval=30
    local max_checks=$((timeout_minutes * 60 / check_interval))
    local check_count=0
    
    log "Monitoring container: $container_name (timeout: ${timeout_minutes}m)"
    
    while [[ $check_count -lt $max_checks ]]; do
        local status=$(az container show \
            --resource-group "$RESOURCE_GROUP" \
            --name "$container_name" \
            --query "containers[0].instanceView.currentState.state" \
            --output tsv 2>/dev/null || echo "Unknown")
        
        log "Container $container_name status: $status"
        
        case "$status" in
            "Succeeded")
                log "Container completed successfully"
                return 0
                ;;
            "Failed"|"Terminated")
                log "Container failed or was terminated"
                return 1
                ;;
            "Running")
                log "Container is running... (check $((check_count + 1))/$max_checks)"
                ;;
            "Pending"|"Unknown")
                log "Container is starting..."
                ;;
            *)
                log "Unknown container status: $status"
                ;;
        esac
        
        sleep $check_interval
        ((check_count++))
    done
    
    log "Container monitoring timed out after ${timeout_minutes} minutes"
    return 2
}

# Get container logs
get_container_logs() {
    local container_name="$1"
    local output_file="${2:-}"
    
    log "Retrieving logs for container: $container_name"
    
    if [[ -n "$output_file" ]]; then
        az container logs \
            --resource-group "$RESOURCE_GROUP" \
            --name "$container_name" \
            > "$output_file" 2>&1
        
        if [[ $? -eq 0 ]]; then
            log "Logs saved to: $output_file"
        else
            log "Failed to retrieve logs"
            return 1
        fi
    else
        az container logs \
            --resource-group "$RESOURCE_GROUP" \
            --name "$container_name"
    fi
}

# Delete container instance
delete_container() {
    local container_name="$1"
    local force="${2:-false}"
    
    log "Deleting container: $container_name"
    
    if [[ "$force" == "true" ]]; then
        az container delete \
            --resource-group "$RESOURCE_GROUP" \
            --name "$container_name" \
            --yes \
            --output table
    else
        az container delete \
            --resource-group "$RESOURCE_GROUP" \
            --name "$container_name" \
            --output table
    fi
    
    if [[ $? -eq 0 ]]; then
        log "Container $container_name deleted successfully"
    else
        log "Failed to delete container $container_name"
        return 1
    fi
}

# List build containers
list_build_containers() {
    local status_filter="${1:-all}"
    
    log "Listing build containers (filter: $status_filter)"
    
    local query="[?starts_with(name, 'build-')]"
    
    case "$status_filter" in
        "running")
            query="[?starts_with(name, 'build-') && containers[0].instanceView.currentState.state=='Running']"
            ;;
        "completed")
            query="[?starts_with(name, 'build-') && containers[0].instanceView.currentState.state=='Succeeded']"
            ;;
        "failed")
            query="[?starts_with(name, 'build-') && (containers[0].instanceView.currentState.state=='Failed' || containers[0].instanceView.currentState.state=='Terminated')]"
            ;;
    esac
    
    az container list \
        --resource-group "$RESOURCE_GROUP" \
        --query "$query.{Name:name,Status:containers[0].instanceView.currentState.state,StartTime:containers[0].instanceView.currentState.startTime,CPU:containers[0].resources.requests.cpu,Memory:containers[0].resources.requests.memoryInGb}" \
        --output table
}

# Cleanup old containers
cleanup_containers() {
    local age_hours="${1:-24}"
    local dry_run="${2:-false}"
    
    log "Cleaning up containers older than $age_hours hours (dry run: $dry_run)"
    
    # Get containers older than specified hours
    local cutoff_time=$(date -u -d "$age_hours hours ago" '+%Y-%m-%dT%H:%M:%SZ')
    
    local old_containers=$(az container list \
        --resource-group "$RESOURCE_GROUP" \
        --query "[?starts_with(name, 'build-') && containers[0].instanceView.currentState.startTime < '$cutoff_time'].name" \
        --output tsv)
    
    if [[ -z "$old_containers" ]]; then
        log "No containers found for cleanup"
        return 0
    fi
    
    local container_count=$(echo "$old_containers" | wc -l)
    log "Found $container_count containers for cleanup"
    
    for container in $old_containers; do
        if [[ "$dry_run" == "true" ]]; then
            log "Would delete container: $container"
        else
            log "Deleting container: $container"
            delete_container "$container" "true"
        fi
    done
}

# Get container metrics
get_container_metrics() {
    local container_name="$1"
    local duration_minutes="${2:-60}"
    
    log "Getting metrics for container: $container_name (last ${duration_minutes}m)"
    
    # Get container resource ID
    local resource_id=$(az container show \
        --resource-group "$RESOURCE_GROUP" \
        --name "$container_name" \
        --query "id" \
        --output tsv)
    
    if [[ -z "$resource_id" ]]; then
        error "Container $container_name not found"
    fi
    
    # Get CPU metrics
    log "CPU Usage:"
    az monitor metrics list \
        --resource "$resource_id" \
        --metric "CpuUsage" \
        --interval PT1M \
        --aggregation Average \
        --start-time "$(date -u -d "$duration_minutes minutes ago" '+%Y-%m-%dT%H:%M:%SZ')" \
        --query "value[0].timeseries[0].data[*].{Time:timeStamp,CPU:average}" \
        --output table
    
    # Get Memory metrics
    log "Memory Usage:"
    az monitor metrics list \
        --resource "$resource_id" \
        --metric "MemoryUsage" \
        --interval PT1M \
        --aggregation Average \
        --start-time "$(date -u -d "$duration_minutes minutes ago" '+%Y-%m-%dT%H:%M:%SZ')" \
        --query "value[0].timeseries[0].data[*].{Time:timeStamp,Memory:average}" \
        --output table
}

# Scale build pool
scale_build_pool() {
    local target_count="$1"
    local current_count=$(az container list \
        --resource-group "$RESOURCE_GROUP" \
        --query "[?starts_with(name, 'voicecode-build-pool-')] | length(@)" \
        --output tsv)
    
    log "Scaling build pool from $current_count to $target_count instances"
    
    if [[ $target_count -gt $current_count ]]; then
        # Scale up
        for ((i=current_count; i<target_count; i++)); do
            log "Creating build pool instance $i"
            
            az container create \
                --resource-group "$RESOURCE_GROUP" \
                --name "voicecode-build-pool-$i" \
                --image "${REGISTRY_NAME}.azurecr.io/voicecode-build-base:latest" \
                --cpu 1 \
                --memory 2 \
                --restart-policy Never \
                --location "East US" \
                --os-type Linux \
                --ip-address None \
                --environment-variables \
                    AGENT_ID="agent-$i" \
                    POOL_SIZE="$target_count" \
                --output none
        done
    elif [[ $target_count -lt $current_count ]]; then
        # Scale down
        for ((i=target_count; i<current_count; i++)); do
            log "Removing build pool instance $i"
            delete_container "voicecode-build-pool-$i" "true"
        done
    else
        log "Build pool already at target size: $target_count"
    fi
}

# Main command handler
main() {
    local command="${1:-help}"
    
    case "$command" in
        "create")
            if [[ $# -lt 6 ]]; then
                error "Usage: $0 create <build_id> <project_type> <source_url> <build_config> <user_id>"
            fi
            check_prerequisites
            create_build_container "$2" "$3" "$4" "$5" "$6"
            ;;
        "monitor")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 monitor <container_name> [timeout_minutes]"
            fi
            check_prerequisites
            monitor_container "$2" "${3:-30}"
            ;;
        "logs")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 logs <container_name> [output_file]"
            fi
            check_prerequisites
            get_container_logs "$2" "${3:-}"
            ;;
        "delete")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 delete <container_name> [force]"
            fi
            check_prerequisites
            delete_container "$2" "${3:-false}"
            ;;
        "list")
            check_prerequisites
            list_build_containers "${2:-all}"
            ;;
        "cleanup")
            check_prerequisites
            cleanup_containers "${2:-24}" "${3:-false}"
            ;;
        "metrics")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 metrics <container_name> [duration_minutes]"
            fi
            check_prerequisites
            get_container_metrics "$2" "${3:-60}"
            ;;
        "scale")
            if [[ $# -lt 2 ]]; then
                error "Usage: $0 scale <target_count>"
            fi
            check_prerequisites
            scale_build_pool "$2"
            ;;
        "help"|*)
            cat << EOF
VoiceCode Container Management

Usage: $0 <command> [options]

Commands:
  create <build_id> <project_type> <source_url> <build_config> <user_id>
                        Create a new build container
  monitor <name> [timeout]  Monitor container status
  logs <name> [file]        Get container logs
  delete <name> [force]     Delete container
  list [status]             List containers (all|running|completed|failed)
  cleanup [hours] [dry_run] Cleanup old containers
  metrics <name> [minutes]  Get container metrics
  scale <count>             Scale build pool
  help                      Show this help

Environment Variables:
  AZURE_RESOURCE_GROUP      Azure resource group name
  AZURE_CONTAINER_REGISTRY  Container registry name
  AZURE_SUBSCRIPTION_ID     Azure subscription ID
  BUILD_STORAGE_ACCOUNT     Build storage account name
  AZURE_CLIENT_ID           Service principal client ID
  AZURE_CLIENT_SECRET       Service principal secret
  AZURE_TENANT_ID           Azure tenant ID

Examples:
  $0 create build-123 dotnet https://github.com/user/repo.git Release user456
  $0 monitor build-123-abcd 45
  $0 list running
  $0 cleanup 48 true
  $0 scale 5
EOF
            ;;
    esac
}

# Run main function with all arguments
main "$@"