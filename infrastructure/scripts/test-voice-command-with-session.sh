#!/bin/bash

# Test voice command with session ID

SESSION_ID="test-session-$(date +%s)"
TRANSCRIPTION="Create a simple React component that displays hello world"

# Use simple token for testing
echo "Using test authentication..."
TOKEN="test-token-$(date +%s)"

echo "Session ID: $SESSION_ID"
echo "Sending voice command: $TRANSCRIPTION"

# Send to Router
ROUTER_URL="https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io"

RESPONSE=$(curl -s -X POST "${ROUTER_URL}/api/voicecommand/process" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"transcription\": \"$TRANSCRIPTION\",
    \"sessionId\": \"$SESSION_ID\",
    \"timestamp\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"
  }")

echo "Router response:"
echo "$RESPONSE" | jq .

# Extract task ID
TASK_ID=$(echo "$RESPONSE" | jq -r '.taskId')
echo "Task ID: $TASK_ID"

# Wait for processing
echo "Waiting for processing..."
sleep 10

# Check logs for session ID propagation
echo "\nChecking Router logs..."
az containerapp logs show --name voicecode-router --resource-group voicecode-rg --tail 20 | grep -i "$SESSION_ID" | tail -5

echo "\nChecking Dispatcher logs..."
az containerapp logs show --name voicecode-dispatcher --resource-group voicecode-rg --tail 20 | grep -i "$SESSION_ID" | tail -5

echo "\nChecking Voice Intelligence logs..."
az containerapp logs show --name voicecode-voice-intelligence --resource-group voicecode-rg --tail 20 | grep -i "$SESSION_ID" | tail -5

echo "\nChecking TTS logs..."
az containerapp logs show --name voicecode-tts --resource-group voicecode-rg --tail 20 | grep -i "$SESSION_ID" | tail -5

echo "\nChecking for audio responses..."
az containerapp logs show --name voicecode-dispatcher --resource-group voicecode-rg --tail 30 | grep -E "(audio-response|AudioResponse|Sending audio)" | tail -5