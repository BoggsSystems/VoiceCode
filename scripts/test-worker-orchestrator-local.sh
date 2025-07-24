#!/bin/bash

# Test Worker and Orchestrator services locally with docker-compose

set -e

PROJECT_ROOT="/Users/jeffboggs/VoiceCode"

echo "========================================="
echo "Testing Worker and Orchestrator Services Locally"
echo "========================================="

# Check if docker-compose.mcp.yml exists
if [ ! -f "$PROJECT_ROOT/docker-compose.mcp.yml" ]; then
    echo "ERROR: docker-compose.mcp.yml not found"
    exit 1
fi

# Start services
echo "Starting services with docker-compose..."
cd $PROJECT_ROOT
docker-compose -f docker-compose.mcp.yml up -d orchestrator worker

# Wait for services to start
echo "Waiting for services to start..."
sleep 10

# Check service health
echo ""
echo "Checking service health..."
echo -n "Orchestrator: "
curl -s http://localhost:5010/health || echo "FAILED"
echo ""
echo -n "Worker: "
curl -s http://localhost:5011/health || echo "FAILED"

# Test task submission
echo ""
echo "Testing task submission..."
TASK_RESPONSE=$(curl -s -X POST http://localhost:5010/api/orchestration/submit-task \
  -H "Content-Type: application/json" \
  -d '{
    "type": "code_generation",
    "prompt": "Create a simple hello world function in Python",
    "context": {
      "language": "python",
      "framework": "none"
    }
  }' || echo "FAILED")

if [ "$TASK_RESPONSE" != "FAILED" ]; then
    echo "Task submitted successfully:"
    echo $TASK_RESPONSE | jq '.'
    
    # Extract task ID
    TASK_ID=$(echo $TASK_RESPONSE | jq -r '.taskId' 2>/dev/null || echo "")
    
    if [ ! -z "$TASK_ID" ]; then
        echo ""
        echo "Waiting for task to complete..."
        sleep 5
        
        # Check task status
        echo "Checking task status..."
        curl -s http://localhost:5010/api/orchestration/task/$TASK_ID/status | jq '.'
    fi
else
    echo "Task submission failed"
fi

# Show logs
echo ""
echo "========================================="
echo "Service Logs"
echo "========================================="
echo "Orchestrator logs:"
docker-compose -f docker-compose.mcp.yml logs --tail=20 orchestrator
echo ""
echo "Worker logs:"
docker-compose -f docker-compose.mcp.yml logs --tail=20 worker

# Cleanup option
echo ""
read -p "Do you want to stop the services? (y/n): " STOP_SERVICES
if [ "$STOP_SERVICES" = "y" ]; then
    docker-compose -f docker-compose.mcp.yml down
    echo "Services stopped"
fi