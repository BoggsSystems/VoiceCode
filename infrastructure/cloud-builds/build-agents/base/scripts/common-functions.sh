#!/bin/bash

# Common functions for VoiceCode build agents

# Security functions
sanitize_input() {
    local input="$1"
    # Remove potentially dangerous characters
    echo "$input" | sed 's/[;&|`$(){}[\]<>]//g'
}

validate_project_path() {
    local path="$1"
    # Ensure path is within allowed directories
    case "$path" in
        /workspace/*|/artifacts/*|/cache/*)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

# Resource monitoring
check_system_resources() {
    local max_memory_mb=4096
    local max_cpu_percent=80
    
    # Check memory usage
    local memory_mb=$(free -m | awk 'NR==2{printf "%.0f", $3}')
    if [[ $memory_mb -gt $max_memory_mb ]]; then
        return 1
    fi
    
    # Check CPU usage
    local cpu_percent=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)
    if (( $(echo "$cpu_percent > $max_cpu_percent" | bc -l) )); then
        return 1
    fi
    
    return 0
}

# File operations
safe_extract() {
    local archive="$1"
    local destination="$2"
    local max_files=1000
    local max_size_mb=100
    
    # Validate destination
    if ! validate_project_path "$destination"; then
        return 1
    fi
    
    # Check archive size
    local archive_size_mb=$(du -m "$archive" | cut -f1)
    if [[ $archive_size_mb -gt $max_size_mb ]]; then
        return 1
    fi
    
    # Extract with limits
    case "$archive" in
        *.tar.gz|*.tgz)
            tar -tzf "$archive" | head -n $max_files | tar -xzf "$archive" -C "$destination" -T -
            ;;
        *.zip)
            unzip -j "$archive" -d "$destination"
            ;;
        *)
            return 1
            ;;
    esac
}

# Network functions
safe_download() {
    local url="$1"
    local destination="$2"
    local max_size_mb=100
    
    # Validate URL
    if [[ ! "$url" =~ ^https?:// ]]; then
        return 1
    fi
    
    # Download with size limit
    wget --max-redirect=3 \
         --timeout=30 \
         --tries=3 \
         --no-check-certificate \
         --quota="${max_size_mb}m" \
         -O "$destination" \
         "$url"
}

# Process management
run_with_timeout() {
    local timeout_seconds="$1"
    shift
    local command=("$@")
    
    timeout "$timeout_seconds" "${command[@]}"
}

kill_process_tree() {
    local pid="$1"
    local children=$(pgrep -P "$pid" 2>/dev/null || true)
    
    for child in $children; do
        kill_process_tree "$child"
    done
    
    kill "$pid" 2>/dev/null || true
}

# Logging functions
log_build_event() {
    local event_type="$1"
    local message="$2"
    local build_id="${BUILD_ID:-unknown}"
    
    local log_entry="{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"build_id\":\"$build_id\",\"event_type\":\"$event_type\",\"message\":\"$message\"}"
    
    echo "$log_entry" >> "${WORKSPACE_DIR}/logs/build.log"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] [$event_type] $message" >&2
}

log_performance_metric() {
    local metric_name="$1"
    local metric_value="$2"
    local build_id="${BUILD_ID:-unknown}"
    
    local metric_entry="{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"build_id\":\"$build_id\",\"metric\":\"$metric_name\",\"value\":$metric_value}"
    
    echo "$metric_entry" >> "${WORKSPACE_DIR}/logs/metrics.log"
}

# Build validation
validate_build_output() {
    local output_dir="$1"
    local project_type="$2"
    
    if [[ ! -d "$output_dir" ]]; then
        return 1
    fi
    
    case "$project_type" in
        "dotnet")
            # Check for compiled assemblies
            find "$output_dir" -name "*.dll" -o -name "*.exe" | head -1 | grep -q .
            ;;
        "nodejs")
            # Check for built files or dist directory
            [[ -d "$output_dir/dist" ]] || [[ -f "$output_dir/index.js" ]]
            ;;
        "java")
            # Check for JAR files
            find "$output_dir" -name "*.jar" | head -1 | grep -q .
            ;;
        "python")
            # Check for wheel or egg files
            find "$output_dir" -name "*.whl" -o -name "*.egg" | head -1 | grep -q .
            ;;
        *)
            # Generic check - ensure output directory is not empty
            [[ -n "$(ls -A "$output_dir")" ]]
            ;;
    esac
}

# Security scanning
scan_for_secrets() {
    local directory="$1"
    local patterns=(
        "password\s*=\s*['\"][^'\"]+['\"]"
        "api[_-]?key\s*=\s*['\"][^'\"]+['\"]"
        "secret\s*=\s*['\"][^'\"]+['\"]"
        "token\s*=\s*['\"][^'\"]+['\"]"
        "-----BEGIN[[:space:]]+.*PRIVATE KEY-----"
    )
    
    for pattern in "${patterns[@]}"; do
        if grep -r -i -E "$pattern" "$directory" >/dev/null 2>&1; then
            log_build_event "SECURITY_WARNING" "Potential secret found matching pattern: $pattern"
            return 1
        fi
    done
    
    return 0
}

scan_for_malicious_code() {
    local directory="$1"
    local suspicious_patterns=(
        "eval\s*\("
        "exec\s*\("
        "system\s*\("
        "shell_exec\s*\("
        "passthru\s*\("
        "rm\s+-rf\s+/"
        "wget.*\|\s*sh"
        "curl.*\|\s*sh"
    )
    
    for pattern in "${suspicious_patterns[@]}"; do
        if grep -r -E "$pattern" "$directory" >/dev/null 2>&1; then
            log_build_event "SECURITY_WARNING" "Suspicious code pattern found: $pattern"
            return 1
        fi
    done
    
    return 0
}

# Cleanup functions
cleanup_temp_files() {
    local temp_dirs=("/tmp" "/var/tmp")
    
    for temp_dir in "${temp_dirs[@]}"; do
        find "$temp_dir" -type f -name "*voicecode*" -mtime +1 -delete 2>/dev/null || true
    done
}

limit_file_count() {
    local directory="$1"
    local max_files="${2:-10000}"
    
    local file_count=$(find "$directory" -type f | wc -l)
    if [[ $file_count -gt $max_files ]]; then
        log_build_event "ERROR" "File count limit exceeded: $file_count > $max_files"
        return 1
    fi
    
    return 0
}

# Error handling
handle_build_error() {
    local exit_code="$1"
    local error_message="$2"
    
    log_build_event "ERROR" "Build failed with exit code $exit_code: $error_message"
    
    # Capture system state
    log_performance_metric "memory_usage_mb" "$(free -m | awk 'NR==2{print $3}')"
    log_performance_metric "disk_usage_percent" "$(df /workspace | awk 'NR==2{print $5}' | sed 's/%//')"
    
    # Cleanup
    cleanup_temp_files
    
    return "$exit_code"
}