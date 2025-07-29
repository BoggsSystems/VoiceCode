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
    
    // Audio playback
    @Published var audioQueue: [AudioQueueItem] = []
    @Published var isPlayingAudio = false
    @Published var currentPlayingAudio: AudioQueueItem?
    private var playedAudioUrls = Set<String>()
    
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
        print("🎵 VoiceChatViewModel: Setting up SignalR bindings")
        
        // Bind SignalR connection status
        signalRService.$isConnected
            .receive(on: DispatchQueue.main)
            .assign(to: &$isConnected)
        print("🎵 VoiceChatViewModel: SignalR connection status binding set up")
        
        // Listen for transcription results
        signalRService.transcriptionReceived
            .receive(on: DispatchQueue.main)
            .sink { [weak self] transcription in
                print("🎵 VoiceChatViewModel: Transcription received from SignalR")
                self?.handleTranscription(transcription)
            }
            .store(in: &cancellables)
        print("🎵 VoiceChatViewModel: Transcription listener set up")
        
        // Listen for audio responses
        signalRService.audioResponseReceived
            .receive(on: DispatchQueue.main)
            .sink { [weak self] audioResponse in
                print("🎵 VoiceChatViewModel: Audio response received from SignalR")
                print("🎵 VoiceChatViewModel: Audio response ID: \(audioResponse.id)")
                print("🎵 VoiceChatViewModel: Audio response URL: \(audioResponse.audioUrl)")
                self?.handleAudioResponse(audioResponse)
            }
            .store(in: &cancellables)
        print("🎵 VoiceChatViewModel: Audio response listener set up")
        
        // Listen for text responses
        signalRService.textResponseReceived
            .receive(on: DispatchQueue.main)
            .sink { [weak self] response in
                print("🎵 VoiceChatViewModel: Text response received from SignalR")
                self?.currentResponse = response.text
            }
            .store(in: &cancellables)
        print("🎵 VoiceChatViewModel: Text response listener set up")
        
        print("🎵 VoiceChatViewModel: All SignalR bindings set up successfully")
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
        
        // Connect to SignalR
        print("🎵 VoiceChatViewModel: Connecting to SignalR...")
        do {
            try await signalRService.connect()
            print("✅ VoiceChatViewModel: SignalR connection successful")
        } catch {
            print("❌ VoiceChatViewModel: ===== SIGNALR CONNECTION FAILED =====")
            print("❌ VoiceChatViewModel: Timestamp: \(Date())")
            print("❌ VoiceChatViewModel: Error type: \(type(of: error))")
            print("❌ VoiceChatViewModel: Error: \(error)")
            print("❌ VoiceChatViewModel: Error description: \(error.localizedDescription)")
            errorMessage = "Failed to connect: \(error.localizedDescription)"
            print("❌ VoiceChatViewModel: Error message set: \(errorMessage ?? "nil")")
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
            
            // Send to backend via SignalR
            print("🎵 VoiceChatViewModel: Calling signalRService.sendAudioData()")
            try await signalRService.sendAudioData(audioData)
            print("✅ VoiceChatViewModel: Audio data sent to SignalR successfully")
            print("✅ VoiceChatViewModel: ===== RECORDING STOPPED AND SENT =====")
            
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