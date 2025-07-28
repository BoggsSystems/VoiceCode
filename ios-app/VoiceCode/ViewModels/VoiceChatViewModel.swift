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
        // Bind SignalR connection status
        signalRService.$isConnected
            .receive(on: DispatchQueue.main)
            .assign(to: &$isConnected)
        
        // Listen for transcription results
        signalRService.transcriptionReceived
            .receive(on: DispatchQueue.main)
            .sink { [weak self] transcription in
                self?.handleTranscription(transcription)
            }
            .store(in: &cancellables)
        
        // Listen for audio responses
        signalRService.audioResponseReceived
            .receive(on: DispatchQueue.main)
            .sink { [weak self] audioResponse in
                self?.handleAudioResponse(audioResponse)
            }
            .store(in: &cancellables)
        
        // Listen for text responses
        signalRService.textResponseReceived
            .receive(on: DispatchQueue.main)
            .sink { [weak self] response in
                self?.currentResponse = response.text
            }
            .store(in: &cancellables)
    }
    
    func initialize() async {
        // Request microphone permission
        let hasPermission = await audioService.requestMicrophonePermission()
        if !hasPermission {
            showPermissionAlert = true
            return
        }
        
        // Connect to SignalR
        do {
            try await signalRService.connect()
        } catch {
            errorMessage = "Failed to connect: \(error.localizedDescription)"
        }
    }
    
    func toggleRecording() async {
        if isRecording {
            await stopRecording()
        } else {
            await startRecording()
        }
    }
    
    private func startRecording() async {
        do {
            // Clear previous results
            currentTranscript = ""
            currentResponse = ""
            
            // Start recording
            try await audioService.startRecording()
            isRecording = true
            status = .listening
        } catch {
            errorMessage = "Failed to start recording: \(error.localizedDescription)"
            status = .error(errorMessage ?? "Unknown error")
        }
    }
    
    private func stopRecording() async {
        do {
            status = .processing
            isRecording = false
            
            // Stop recording and get audio data
            let audioData = try await audioService.stopRecording()
            
            // Send to backend via SignalR
            try await signalRService.sendAudioData(audioData)
            
        } catch {
            errorMessage = "Failed to process audio: \(error.localizedDescription)"
            status = .error(errorMessage ?? "Unknown error")
        }
    }
    
    private func handleTranscription(_ transcription: TranscriptionResult) {
        currentTranscript = transcription.text
        status = .processing
    }
    
    private func handleAudioResponse(_ response: AudioResponse) {
        // Check for duplicate
        if playedAudioUrls.contains(response.audioUrl) {
            print("[VoiceChatViewModel] Skipping duplicate audio URL: \(response.audioUrl)")
            return
        }
        
        let audioItem = AudioQueueItem(
            id: response.id,
            audioUrl: response.audioUrl,
            text: response.text
        )
        
        audioQueue.append(audioItem)
        
        // If not currently playing, start playback
        if !isPlayingAudio {
            playNextAudio()
        }
    }
    
    private func playNextAudio() {
        guard !audioQueue.isEmpty else {
            isPlayingAudio = false
            currentPlayingAudio = nil
            status = .idle
            return
        }
        
        let audioItem = audioQueue.removeFirst()
        currentPlayingAudio = audioItem
        playedAudioUrls.insert(audioItem.audioUrl)
        
        // Keep only last 100 played URLs
        if playedAudioUrls.count > 100 {
            playedAudioUrls = Set(Array(playedAudioUrls).suffix(100))
        }
        
        isPlayingAudio = true
        status = .speaking
        
        Task {
            do {
                try await audioService.playAudio(from: audioItem.audioUrl)
                await MainActor.run {
                    playNextAudio() // Play next in queue
                }
            } catch {
                print("[VoiceChatViewModel] Error playing audio: \(error)")
                await MainActor.run {
                    playNextAudio() // Skip to next on error
                }
            }
        }
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