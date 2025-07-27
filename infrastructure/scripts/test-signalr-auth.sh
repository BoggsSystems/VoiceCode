#!/bin/bash

# Test SignalR authentication with the Dispatcher service

DISPATCHER_URL="https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io"
TOKEN="test-token-123"

echo "========================================="
echo "Testing SignalR Authentication"
echo "========================================="

# Test auth check endpoint
echo "1. Testing auth check endpoint (no auth)..."
curl -X GET "$DISPATCHER_URL/api/authtest/check" \
  -H "Accept: application/json" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "2. Testing auth check endpoint (with token)..."
curl -X GET "$DISPATCHER_URL/api/authtest/check" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "3. Testing protected endpoint (no auth)..."
curl -X GET "$DISPATCHER_URL/api/authtest/check-protected" \
  -H "Accept: application/json" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "4. Testing protected endpoint (with token)..."
curl -X GET "$DISPATCHER_URL/api/authtest/check-protected" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "5. Testing SignalR negotiate endpoint..."
curl -X POST "$DISPATCHER_URL/hubs/voice/negotiate?negotiateVersion=1" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -H "Content-Type: application/json" \
  -w "\nHTTP Status: %{http_code}\n\n"

echo "========================================="
echo "Test complete!"
echo "=========================================