#!/bin/bash

# Create Service Bus queue for Observer Service

RESOURCE_GROUP="voicecode-rg"
NAMESPACE="voicecodebus-0724"
QUEUE_NAME="sdk-stream-events"

# Create the SDK stream events queue
echo "Creating Service Bus queue: $QUEUE_NAME"
az servicebus queue create \
    --resource-group $RESOURCE_GROUP \
    --namespace-name $NAMESPACE \
    --name $QUEUE_NAME \
    --max-size 1024 \
    --default-message-time-to-live P1D \
    --max-delivery-count 10

# TTS queue already exists
echo "Using existing tts-requests queue for narrations"

echo "Observer Service resources created successfully"