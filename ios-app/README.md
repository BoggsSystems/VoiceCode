# VoiceCode iOS App

A native iOS application providing reliable voice-powered AI assistance with seamless audio playback and Bluetooth/AirPods support.

## Features

- **Reliable Audio Playback**: Native iOS audio APIs for instant, restriction-free playback
- **Bluetooth & AirPods Support**: Automatic audio routing and control support
- **Background Audio**: Continue playing responses with screen off
- **MVVM Architecture**: Clean, maintainable code structure
- **SignalR Integration**: Real-time communication with backend services
- **Secure Authentication**: Keychain storage for credentials
- **Duplicate Audio Prevention**: Smart tracking to prevent playing same audio twice

## Architecture

```
ios-app/VoiceCode/
├── Models/              # Data models
├── Views/               # SwiftUI views
├── ViewModels/          # View models (business logic)
├── Services/            # Backend services, audio, SignalR
├── Utilities/           # Helper functions and extensions
├── Resources/           # Assets, fonts, etc.
└── Supporting Files/    # Info.plist, etc.
```

## Setup Instructions

### Prerequisites

1. macOS with Xcode 15.0 or later
2. iOS 16.0+ deployment target
3. Apple Developer account (for device testing)

### Installation

1. Open `VoiceCode.xcodeproj` in Xcode
2. Install dependencies via Swift Package Manager:
   - SignalR-Client-Swift
   - Alamofire
   - KeychainAccess

3. Configure your backend URL in `NetworkService.swift`:
   ```swift
   private let baseURL = "YOUR_BACKEND_URL"
   ```

4. Build and run on simulator or device

### Configuration

The app uses the following backend endpoints:
- Authentication: `/api/auth/login/simple`
- SignalR Hub: `https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io/hubs/voice`

## Key Components

### Audio Service
- Records audio in WAV format (16kHz, mono)
- Plays audio from URLs with automatic caching
- Handles microphone permissions
- Monitors audio levels during recording

### SignalR Service
- Manages real-time connection to backend
- Handles audio transcription events
- Receives audio response URLs
- Maintains session state

### Voice Chat View Model
- Orchestrates recording, processing, and playback
- Manages audio queue with duplicate prevention
- Handles UI state transitions
- Coordinates between services

## Usage

1. **Login**: Enter credentials on launch
2. **Voice Chat**: 
   - Tap microphone button to start recording
   - Release or tap again to stop
   - Audio responses play automatically
3. **Settings**: Configure audio preferences and view debug info

## Deployment

1. Configure bundle identifier and signing in Xcode
2. Archive for distribution
3. Upload to App Store Connect or distribute via TestFlight

## Testing

Run unit tests:
```bash
xcodebuild test -scheme VoiceCode -destination 'platform=iOS Simulator,name=iPhone 15'
```

## Troubleshooting

### Audio Not Playing
- Check audio session configuration
- Verify Bluetooth permissions
- Ensure background audio capability is enabled

### SignalR Connection Issues
- Verify backend URL is correct
- Check authentication token
- Monitor debug panel for connection status

## Future Enhancements

- [ ] Siri Shortcuts integration
- [ ] Home screen widget
- [ ] Offline mode with queued requests
- [ ] Voice activity detection
- [ ] Customizable wake words

## License

Copyright © 2025 VoiceCode. All rights reserved.