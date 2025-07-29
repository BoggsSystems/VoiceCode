import Foundation
import AVFoundation
import Combine

@MainActor
class VoiceChatViewModel: ObservableObject {
    @Published var status: VoiceStatus = .idle
    @Published var isRecording = false
    @Published var isProcessing = false
    @Published var isConnected = false
    @Published var currentTranscript = ""
    @Published var currentResponse = ""
    @Published var errorMessage: String?
    @Published var showPermissionAlert = false
    @Published var messages: [Message] = []
    
    // Audio playback
    @Published var audioQueue: [AudioQueueItem] = []
    @Published var isPlayingAudio = false
    @Published var currentPlayingAudio: AudioQueueItem?
    private var playedAudioUrls = Set<String>()
    
    // Session tracking
    private let sessionId = "ios-\(Date().timeIntervalSince1970)-\(UUID().uuidString.prefix(8))"
    
    // Services
    private let audioService = AudioService.shared
    private let signalRService = SignalRService.shared
    private let networkService = NetworkService.shared
    
    // Cancellables
    private var cancellables = Set<AnyCancellable>()
    
    init() {
        setupBindings()
    }
    
    private func setupBindings() {
        print("🎵 VoiceChatViewModel: Setting up bindings for HTTP mode")
        
        // For HTTP mode, we check connection status based on auth token
        isConnected = KeychainService.shared.getAuthToken() != nil
        print("🎵 VoiceChatViewModel: Connection status based on auth token: \(isConnected)")
        
        // We can still optionally use SignalR for real-time updates
        // but the primary audio submission will go through HTTP
        
        print("🎵 VoiceChatViewModel: Bindings set up for HTTP mode")
    }
    
    func initialize() async {
        print("🎵 VoiceChatViewModel: ===== INITIALIZING VOICE CHAT =====")
        print("🎵 VoiceChatViewModel: Timestamp: \(Date())")
        
        // Request microphone permission
        print("🎵 VoiceChatViewModel: Requesting microphone permission...")
        let hasPermission = await audioService.requestMicrophonePermission()
        if !hasPermission {
            print("❌ VoiceChatViewModel: Microphone permission denied")
            print("❌ VoiceChatViewModel: Showing permission alert")
            showPermissionAlert = true
            return
        }
        
        print("✅ VoiceChatViewModel: Microphone permission granted")
        
        // Check authentication status
        print("🎵 VoiceChatViewModel: Checking authentication status...")
        if KeychainService.shared.getAuthToken() != nil {
            print("✅ VoiceChatViewModel: Authentication token found")
            isConnected = true
            status = .ready
        } else {
            print("❌ VoiceChatViewModel: No authentication token found")
            errorMessage = "Not authenticated. Please log in."
            status = .error(errorMessage ?? "Not authenticated")
        }
        
        print("🎵 VoiceChatViewModel: ===== VOICE CHAT INITIALIZATION COMPLETE =====")
    }
    
    func toggleRecording() async {
        print("🎵 VoiceChatViewModel: ===== TOGGLE RECORDING CALLED =====")
        print("🎵 VoiceChatViewModel: Timestamp: \(Date())")
        print("🎵 VoiceChatViewModel: Current recording state: \(isRecording)")
        print("🎵 VoiceChatViewModel: Current connection state: \(isConnected)")
        
        if isRecording {
            print("🎵 VoiceChatViewModel: Currently recording, stopping...")
            await stopRecording()
        } else {
            print("🎵 VoiceChatViewModel: Not recording, starting...")
            await startRecording()
        }
    }
    
    private func startRecording() async {
        print("🎵 VoiceChatViewModel: ===== STARTING RECORDING =====")
        print("🎵 VoiceChatViewModel: Timestamp: \(Date())")
        
        do {
            // Clear previous results
            print("🎵 VoiceChatViewModel: Clearing previous results")
            currentTranscript = ""
            currentResponse = ""
            
            // Start recording
            print("🎵 VoiceChatViewModel: Calling audioService.startRecording()")
            try await audioService.startRecording()
            print("✅ VoiceChatViewModel: Audio recording started successfully")
            
            isRecording = true
            status = .listening
            print("✅ VoiceChatViewModel: Recording state updated")
            print("✅ VoiceChatViewModel: Status set to: \(status.displayText)")
            print("✅ VoiceChatViewModel: ===== RECORDING STARTED =====")
        } catch {
            print("❌ VoiceChatViewModel: ===== RECORDING START FAILED =====")
            print("❌ VoiceChatViewModel: Timestamp: \(Date())")
            print("❌ VoiceChatViewModel: Error type: \(type(of: error))")
            print("❌ VoiceChatViewModel: Error: \(error)")
            print("❌ VoiceChatViewModel: Error description: \(error.localizedDescription)")
            
            errorMessage = "Failed to start recording: \(error.localizedDescription)"
            status = .error(errorMessage ?? "Unknown error")
            print("❌ VoiceChatViewModel: Error message set: \(errorMessage ?? "nil")")
            print("❌ VoiceChatViewModel: Status set to error")
        }
    }
    
    private func stopRecording() async {
        print("🎵 VoiceChatViewModel: ===== STOPPING RECORDING =====")
        print("🎵 VoiceChatViewModel: Timestamp: \(Date())")
        
        do {
            status = .processing
            isRecording = false
            print("🎵 VoiceChatViewModel: Updated status to processing")
            print("🎵 VoiceChatViewModel: Set isRecording to false")
            
            // Stop recording and get audio data
            print("🎵 VoiceChatViewModel: Calling audioService.stopRecording()")
            let audioData = try await audioService.stopRecording()
            print("✅ VoiceChatViewModel: Audio recording stopped successfully")
            print("✅ VoiceChatViewModel: Audio data size: \(audioData.count) bytes")
            print("✅ VoiceChatViewModel: Audio data size in KB: \(Double(audioData.count) / 1024.0) KB")
            
            // Use HTTP endpoint instead of SignalR
            print("🎵 VoiceChatViewModel: Using HTTP endpoint for audio submission")
            
            // Step 1: Transcribe audio
            print("🎵 VoiceChatViewModel: Calling NetworkService.transcribeAudio()")
            let transcriptionResult = try await NetworkService.shared.transcribeAudio(audioData)
            print("✅ VoiceChatViewModel: Transcription received: \(transcriptionResult.text)")
            
            // Update UI with transcription
            await MainActor.run {
                self.currentTranscript = transcriptionResult.text
                self.addMessage(content: transcriptionResult.text, type: .user)
            }
            
            // Step 2: Process voice command
            print("🎵 VoiceChatViewModel: Calling NetworkService.processVoiceCommand()")
            let commandResult = try await NetworkService.shared.processVoiceCommand(
                transcriptionResult.text,
                sessionId: sessionId
            )
            print("✅ VoiceChatViewModel: Voice command processed successfully")
            print("✅ VoiceChatViewModel: Task ID: \(commandResult.taskId)")
            print("✅ VoiceChatViewModel: Status: \(commandResult.status)")
            
            // Update status
            await MainActor.run {
                self.status = .ready
                if let message = commandResult.message {
                    self.addMessage(content: message, type: .assistant)
                }
            }
            
            print("✅ VoiceChatViewModel: ===== RECORDING STOPPED AND PROCESSED =====")
            
        } catch {
            print("❌ VoiceChatViewModel: ===== RECORDING STOP FAILED =====")
            print("❌ VoiceChatViewModel: Timestamp: \(Date())")
            print("❌ VoiceChatViewModel: Error type: \(type(of: error))")
            print("❌ VoiceChatViewModel: Error: \(error)")
            print("❌ VoiceChatViewModel: Error description: \(error.localizedDescription)")
            
            errorMessage = "Failed to process audio: \(error.localizedDescription)"
            status = .error(errorMessage ?? "Unknown error")
            print("❌ VoiceChatViewModel: Error message set: \(errorMessage ?? "nil")")
            print("❌ VoiceChatViewModel: Status set to error")
        }
    }
    
    private func handleTranscription(_ transcription: TranscriptionResult) {
        currentTranscript = transcription.text
        status = .processing
    }
    
    private func handleAudioResponse(_ response: AudioResponse) {
        print("🎵 VoiceChatViewModel: ===== AUDIO RESPONSE RECEIVED IN VIEWMODEL =====")
        print("🎵 VoiceChatViewModel: Timestamp: \(Date())")
        print("🎵 VoiceChatViewModel: Response ID: \(response.id)")
        print("🎵 VoiceChatViewModel: Audio URL: \(response.audioUrl)")
        print("🎵 VoiceChatViewModel: Audio URL length: \(response.audioUrl.count) characters")
        print("🎵 VoiceChatViewModel: Text content: \(response.text)")
        print("🎵 VoiceChatViewModel: Text length: \(response.text.count) characters")
        print("🎵 VoiceChatViewModel: Current audio queue size: \(audioQueue.count)")
        print("🎵 VoiceChatViewModel: Is currently playing audio: \(isPlayingAudio)")
        print("🎵 VoiceChatViewModel: Played URLs count: \(playedAudioUrls.count)")
        
        // Check for duplicate
        if playedAudioUrls.contains(response.audioUrl) {
            print("⚠️ VoiceChatViewModel: DUPLICATE AUDIO URL DETECTED")
            print("⚠️ VoiceChatViewModel: Skipping duplicate audio URL: \(response.audioUrl)")
            print("⚠️ VoiceChatViewModel: This URL has already been played")
            return
        }
        
        print("✅ VoiceChatViewModel: Audio URL is new, proceeding with processing")
        
        let audioItem = AudioQueueItem(
            id: response.id,
            audioUrl: response.audioUrl,
            text: response.text
        )
        
        print("🎵 VoiceChatViewModel: Created AudioQueueItem")
        print("🎵 VoiceChatViewModel: AudioQueueItem ID: \(audioItem.id)")
        print("🎵 VoiceChatViewModel: AudioQueueItem URL: \(audioItem.audioUrl)")
        print("🎵 VoiceChatViewModel: AudioQueueItem text: \(audioItem.text)")
        
        audioQueue.append(audioItem)
        print("🎵 VoiceChatViewModel: Added audio item to queue")
        print("🎵 VoiceChatViewModel: New audio queue size: \(audioQueue.count)")
        
        // If not currently playing, start playback
        if !isPlayingAudio {
            print("🎵 VoiceChatViewModel: No audio currently playing, starting playback")
            playNextAudio()
        } else {
            print("🎵 VoiceChatViewModel: Audio is currently playing, item queued for later playback")
        }
        
        print("🎵 VoiceChatViewModel: ===== AUDIO RESPONSE PROCESSING COMPLETE =====")
    }
    
    private func playNextAudio() {
        print("🎵 VoiceChatViewModel: ===== PLAY NEXT AUDIO CALLED =====")
        print("🎵 VoiceChatViewModel: Timestamp: \(Date())")
        print("🎵 VoiceChatViewModel: Audio queue size: \(audioQueue.count)")
        print("🎵 VoiceChatViewModel: Is currently playing: \(isPlayingAudio)")
        
        guard !audioQueue.isEmpty else {
            print("🎵 VoiceChatViewModel: Audio queue is empty")
            print("🎵 VoiceChatViewModel: Setting isPlayingAudio to false")
            print("🎵 VoiceChatViewModel: Setting status to idle")
            isPlayingAudio = false
            currentPlayingAudio = nil
            status = .idle
            print("🎵 VoiceChatViewModel: ===== AUDIO PLAYBACK COMPLETE =====")
            return
        }
        
        let audioItem = audioQueue.removeFirst()
        print("🎵 VoiceChatViewModel: Removed audio item from queue")
        print("🎵 VoiceChatViewModel: Audio item ID: \(audioItem.id)")
        print("🎵 VoiceChatViewModel: Audio item URL: \(audioItem.audioUrl)")
        print("🎵 VoiceChatViewModel: Audio item text: \(audioItem.text)")
        print("🎵 VoiceChatViewModel: Remaining queue size: \(audioQueue.count)")
        
        currentPlayingAudio = audioItem
        playedAudioUrls.insert(audioItem.audioUrl)
        print("🎵 VoiceChatViewModel: Set current playing audio")
        print("🎵 VoiceChatViewModel: Added URL to played URLs set")
        print("🎵 VoiceChatViewModel: Total played URLs: \(playedAudioUrls.count)")
        
        // Keep only last 100 played URLs
        if playedAudioUrls.count > 100 {
            print("🎵 VoiceChatViewModel: Played URLs count exceeded 100, trimming to last 100")
            playedAudioUrls = Set(Array(playedAudioUrls).suffix(100))
            print("🎵 VoiceChatViewModel: Trimmed played URLs count: \(playedAudioUrls.count)")
        }
        
        isPlayingAudio = true
        status = .speaking
        print("🎵 VoiceChatViewModel: Set isPlayingAudio to true")
        print("🎵 VoiceChatViewModel: Set status to speaking")
        print("🎵 VoiceChatViewModel: About to start audio playback")
        
        Task {
            do {
                print("🎵 VoiceChatViewModel: Starting audio playback task")
                try await audioService.playAudio(from: audioItem.audioUrl)
                print("🎵 VoiceChatViewModel: Audio playback completed successfully")
                await MainActor.run {
                    print("🎵 VoiceChatViewModel: Audio playback finished, calling playNextAudio")
                    playNextAudio() // Play next in queue
                }
            } catch {
                print("❌ VoiceChatViewModel: ERROR during audio playback")
                print("❌ VoiceChatViewModel: Error details: \(error)")
                print("❌ VoiceChatViewModel: Audio URL that failed: \(audioItem.audioUrl)")
                await MainActor.run {
                    print("🎵 VoiceChatViewModel: Audio playback failed, skipping to next")
                    playNextAudio() // Skip to next on error
                }
            }
        }
        
        print("🎵 VoiceChatViewModel: ===== PLAY NEXT AUDIO SETUP COMPLETE =====")
    }
    
    func skipCurrentAudio() {
        audioService.stopAudio()
        playNextAudio()
    }
    
    func addMessage(content: String, type: Message.MessageType) {
        let message = Message(content: content, type: type)
        messages.append(message)
    }
}

// Models for SignalR responses
struct TranscriptionResult {
    let id: String
    let text: String
    let confidence: Double
}

struct AudioResponse {
    let id: String
    let audioUrl: String
    let text: String
}

struct TextResponse {
    let id: String
    let text: String
}