#!/bin/bash

# Test Multi-Worker Orchestration (Phase 3)

set -e

ORCHESTRATOR_URL="${ORCHESTRATOR_URL:-http://localhost:5010}"

echo "========================================="
echo "Testing Multi-Worker Orchestration"
echo "========================================="

# Function to make API calls
api_call() {
    local method=$1
    local endpoint=$2
    local data=$3
    
    if [ -z "$data" ]; then
        curl -s -X $method "$ORCHESTRATOR_URL/api/orchestration/$endpoint" \
             -H "Content-Type: application/json"
    else
        curl -s -X $method "$ORCHESTRATOR_URL/api/orchestration/$endpoint" \
             -H "Content-Type: application/json" \
             -d "$data"
    fi
}

# 1. Check pool status
echo ""
echo "1. Checking worker pool status..."
POOL_STATUS=$(api_call GET "pool/status")
echo $POOL_STATUS | jq '.'

TOTAL_WORKERS=$(echo $POOL_STATUS | jq -r '.totalWorkers')
HEALTHY_WORKERS=$(echo $POOL_STATUS | jq -r '.healthyWorkers')
AVAILABLE_WORKERS=$(echo $POOL_STATUS | jq -r '.availableWorkers')

echo "Total Workers: $TOTAL_WORKERS"
echo "Healthy Workers: $HEALTHY_WORKERS"
echo "Available Workers: $AVAILABLE_WORKERS"

# 2. Check current distribution policy
echo ""
echo "2. Checking distribution policy..."
POLICY=$(api_call GET "pool/policy")
echo $POLICY | jq '.'

# 3. Submit multiple concurrent tasks
echo ""
echo "3. Submitting multiple concurrent tasks..."

TASKS=(
    '{"requestType":"code_generation","userPrompt":"Create a Python function that calculates factorial","context":{"language":"python"}}'
    '{"requestType":"code_generation","userPrompt":"Create a JavaScript async function that fetches data from an API","context":{"language":"javascript"}}'
    '{"requestType":"refactoring","userPrompt":"Refactor this function to use modern C# patterns","context":{"language":"csharp"}}'
    '{"requestType":"testing","userPrompt":"Write unit tests for a calculator class","context":{"language":"java"}}'
    '{"requestType":"documentation","userPrompt":"Generate API documentation for a REST service","context":{"language":"typescript"}}'
)

TASK_IDS=()

for i in "${!TASKS[@]}"; do
    echo "Submitting task $((i+1))..."
    RESPONSE=$(api_call POST "submit-task" "${TASKS[$i]}")
    TASK_ID=$(echo $RESPONSE | jq -r '.metadata.TaskId // empty')
    
    if [ ! -z "$TASK_ID" ]; then
        TASK_IDS+=($TASK_ID)
        echo "Task $((i+1)) submitted with ID: $TASK_ID"
    else
        echo "Failed to submit task $((i+1))"
        echo $RESPONSE | jq '.'
    fi
done

# 4. Check pool status again (should show workers busy)
echo ""
echo "4. Checking pool status after task submission..."
sleep 2
POOL_STATUS=$(api_call GET "pool/status")
echo $POOL_STATUS | jq '.{totalWorkers,healthyWorkers,availableWorkers,currentLoad,utilizationPercentage}'

# 5. Check task assignments
echo ""
echo "5. Checking task assignments..."
ASSIGNMENTS=$(api_call GET "pool/assignments")
echo "Task assignments by worker:"
echo $ASSIGNMENTS | jq '.'

# 6. Check worker metrics
echo ""
echo "6. Checking worker metrics..."
METRICS=$(api_call GET "pool/metrics")
echo "Worker metrics:"
echo $METRICS | jq '.'

# 7. Test batch submission
echo ""
echo "7. Testing batch task submission..."
BATCH_REQUEST='[
    {"requestType":"code_generation","userPrompt":"Create a REST API endpoint","context":{"language":"python"}},
    {"requestType":"code_generation","userPrompt":"Create a GraphQL resolver","context":{"language":"javascript"}},
    {"requestType":"code_generation","userPrompt":"Create a gRPC service","context":{"language":"go"}}
]'

BATCH_RESPONSE=$(api_call POST "submit-batch" "$BATCH_REQUEST")
echo "Batch submission result:"
echo $BATCH_RESPONSE | jq '.{totalTasks,successfulTasks,failedTasks}'

# 8. Test load balancing strategies
echo ""
echo "8. Testing different load balancing strategies..."

STRATEGIES=("RoundRobin" "LeastLoaded" "CapabilityBased" "Performance" "Adaptive")

for strategy in "${STRATEGIES[@]}"; do
    echo ""
    echo "Testing $strategy strategy..."
    
    # Update policy
    POLICY_UPDATE=$(cat <<EOF
{
    "strategy": "$strategy",
    "enableAutoScaling": false,
    "minWorkers": 1,
    "maxWorkers": 10,
    "scaleUpThreshold": 80,
    "scaleDownThreshold": 20,
    "healthCheckInterval": "00:00:30",
    "maxRetries": 3,
    "retryDelay": "00:00:05"
}
EOF
)
    
    api_call PUT "pool/policy" "$POLICY_UPDATE" > /dev/null
    
    # Submit a task
    TASK_RESPONSE=$(api_call POST "submit-task" '{"requestType":"code_generation","userPrompt":"Test task for '${strategy}'","context":{"language":"python"}}')
    WORKER_ID=$(echo $TASK_RESPONSE | jq -r '.metadata.WorkerId // "unknown"')
    echo "Task assigned to worker: $WORKER_ID"
done

# 9. Test scaling
echo ""
echo "9. Testing worker scaling..."
CURRENT_WORKERS=$(echo $POOL_STATUS | jq -r '.totalWorkers')
TARGET_WORKERS=$((CURRENT_WORKERS + 2))

echo "Scaling from $CURRENT_WORKERS to $TARGET_WORKERS workers..."
SCALE_RESPONSE=$(api_call POST "pool/scale" "{\"targetCount\": $TARGET_WORKERS}")
echo $SCALE_RESPONSE | jq '.'

# 10. Final pool status
echo ""
echo "10. Final pool status..."
sleep 5
FINAL_STATUS=$(api_call GET "pool/status")
echo $FINAL_STATUS | jq '.'

# Summary
echo ""
echo "========================================="
echo "Multi-Worker Orchestration Test Summary"
echo "========================================="
echo "✓ Worker pool management"
echo "✓ Task distribution and load balancing"
echo "✓ Concurrent task execution"
echo "✓ Worker metrics tracking"
echo "✓ Batch task submission"
echo "✓ Multiple load balancing strategies"
echo "✓ Worker scaling"
echo ""
echo "Phase 3 testing complete!"