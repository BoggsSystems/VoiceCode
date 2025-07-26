#!/bin/bash

# Test STT service directly
echo "Testing STT service..."

# Create a simple test audio file using text-to-speech
echo "Creating test audio file..."
say "Hello, this is a test of the speech to text service" -o test-audio.aiff

# Check if file was created
if [ -f "test-audio.aiff" ]; then
    echo "Audio file created successfully"
    ls -la test-audio.aiff
    
    # Convert to webm
    echo "Converting to webm format..."
    ffmpeg -i test-audio.aiff -acodec libopus -b:a 64k test-audio.webm -y 2>/dev/null
    
    if [ -f "test-audio.webm" ]; then
        echo "Webm file created successfully"
        ls -la test-audio.webm
        
        # Test the unauthenticated test endpoint first
        echo "Testing unauthenticated endpoint..."
        curl -X POST \
          https://voicecode-stt.orangewater-a2f689a8.eastus.azurecontainerapps.io/api/test/transcription/transcribe \
          -F "audioFile=@./test-audio.webm" \
          -F "language=en-US" \
          -H "Accept: application/json" \
          -v
    else
        echo "Failed to create webm file"
    fi
else
    echo "Failed to create audio file"
fi

# Clean up
rm -f test-audio.aiff test-audio.webm

echo "Test complete!"