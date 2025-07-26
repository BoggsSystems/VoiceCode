#!/bin/bash

echo "=== VoiceCode Services Log Monitor ==="
echo "Monitoring logs for mobile app testing..."
echo "Press Ctrl+C to stop monitoring"
echo ""

while true; do
    echo "========================================="
    echo "Timestamp: $(date)"
    echo "========================================="
    
    echo ""
    echo "=== DISPATCHER SERVICE ==="
    az container logs --resource-group voicecode-rg --name voicecode-dispatcher 2>/dev/null | tail -20
    
    echo ""
    echo "=== STT SERVICE ==="
    az container logs --resource-group voicecode-rg --name voicecode-dev-eus-stt-ci 2>/dev/null | tail -20
    
    echo ""
    echo "=== VOICE INTELLIGENCE (CLAUDE) SERVICE ==="
    az containerapp logs show --name voicecode-voice-intelligence --resource-group voicecode-rg --tail 20 2>/dev/null | jq -r '.Log' 2>/dev/null | tail -20
    
    echo ""
    echo "=== TTS SERVICE ==="
    az containerapp logs show --name voicecode-tts --resource-group voicecode-rg --tail 20 2>/dev/null | jq -r '.Log' 2>/dev/null | tail -20
    
    echo ""
    echo "Waiting 5 seconds before next check..."
    sleep 5
done