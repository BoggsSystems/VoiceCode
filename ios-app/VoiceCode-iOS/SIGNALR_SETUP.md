# SignalR Setup Instructions for iOS

To enable real SignalR connections in the iOS app, you need to add the SignalR-Client-Swift package to your Xcode project:

## Steps to Add SignalR Package

1. Open `VoiceCode-iOS.xcodeproj` in Xcode

2. In Xcode, go to **File** → **Add Package Dependencies...**

3. In the search field, enter: `https://github.com/moozzyk/SignalR-Client-Swift`

4. Click **Add Package**

5. Select version `0.9.2` or later

6. Make sure the package is added to the `VoiceCode-iOS` target

7. Click **Add Package** to confirm

## Verify Installation

After adding the package, build the project (⌘+B) to ensure SignalR is properly integrated.

## What Changed

The `SignalRService.swift` file has been updated to:
- Import the real `SignalRClient` library
- Use actual `HubConnection` and `HubConnectionBuilder` classes
- Connect to the real SignalR hub at the Azure endpoint
- Handle real-time events (TranscriptionReceived, AudioResponseReady, etc.)
- Send audio data using the same format as the web app

## Testing the Connection

1. Run the app
2. Login with your credentials
3. Navigate to the Voice Chat view
4. The app will now connect to the real SignalR hub
5. Check the Xcode console for connection logs
6. Audio will be sent to the backend for processing