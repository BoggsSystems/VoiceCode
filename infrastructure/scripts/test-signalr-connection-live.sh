#!/bin/bash

# Test SignalR connection with live deployed services

WEBAPP_URL="https://gentle-river-0d0d70a0f.1.azurestaticapps.net"
DISPATCHER_URL="https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io"

echo "========================================="
echo "Testing Live SignalR Connection"
echo "========================================="

echo "1. Testing Dispatcher health endpoint..."
curl -s "$DISPATCHER_URL/health" || echo "Health check returned non-200"
echo -e "\n"

echo "2. Testing SignalR negotiate without auth..."
curl -X POST "$DISPATCHER_URL/hubs/voice/negotiate?negotiateVersion=1" \
  -H "Accept: application/json" \
  -H "Content-Type: application/json" \
  -H "Origin: $WEBAPP_URL" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "3. Testing SignalR negotiate with test token..."
curl -X POST "$DISPATCHER_URL/hubs/voice/negotiate?negotiateVersion=1" \
  -H "Authorization: Bearer test-token-123" \
  -H "Accept: application/json" \
  -H "Content-Type: application/json" \
  -H "Origin: $WEBAPP_URL" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "========================================="
echo "Summary:"
echo "- Webapp URL: $WEBAPP_URL"
echo "- Dispatcher URL: $DISPATCHER_URL"
echo "- Test the app at: $WEBAPP_URL"
echo "- Login and check SignalR status in debug panel"
echo "========================================="