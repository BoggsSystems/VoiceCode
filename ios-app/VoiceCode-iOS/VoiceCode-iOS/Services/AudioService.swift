import Foundation
import AVFoundation
import Combine

class AudioService: NSObject, ObservableObject {
    static let shared = AudioService()
    
    // Audio recording
    private var audioRecorder: AVAudioRecorder?
    private var recordingSession: AVAudioSession?
    private var recordingURL: URL?
    
    // Audio playback
    private var audioPlayer: AVAudioPlayer?
    private var audioSession: AVAudioSession?
    
    // Publishers
    @Published var isRecording = false
    @Published var isPlaying = false
    @Published var audioLevel: Float = 0.0
    
    private var levelTimer: Timer?
    
    override private init() {
        super.init()
        setupAudioSession()
    }
    
    private func setupAudioSession() {
        audioSession = AVAudioSession.sharedInstance()
        
        do {
            try audioSession?.setCategory(.playAndRecord, mode: .default, options: [.defaultToSpeaker, .allowBluetooth])
            try audioSession?.setActive(true)
        } catch {
            print("[AudioService] Failed to setup audio session: \(error)")
        }
    }
    
    func requestMicrophonePermission() async -> Bool {
        print("🎤 AudioService: Requesting microphone permission...")
        let result = await withCheckedContinuation { continuation in
            AVAudioSession.sharedInstance().requestRecordPermission { granted in
                continuation.resume(returning: granted)
            }
        }
        print("🎤 AudioService: Microphone permission result: \(result ? "Granted" : "Denied")")
        return result
    }
    
    func startRecording() async throws {
        print("🎤 AudioService: ===== STARTING AUDIO RECORDING =====")
        print("🎤 AudioService: Timestamp: \(Date())")
        
        let hasPermission = await requestMicrophonePermission()
        guard hasPermission else {
            print("❌ AudioService: Microphone permission denied")
            throw AudioError.microphonePermissionDenied
        }
        
        print("✅ AudioService: Microphone permission verified")
        
        // Setup recording URL
        let documentsPath = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
        recordingURL = documentsPath.appendingPathComponent("recording-\(Date().timeIntervalSince1970).wav")
        print("🎤 AudioService: Recording URL: \(recordingURL?.absoluteString ?? "nil")")
        
        // Configure audio settings for WAV format
        let settings: [String: Any] = [
            AVFormatIDKey: Int(kAudioFormatLinearPCM),
            AVSampleRateKey: 16000.0,
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
            AVEncoderAudioQualityKey: AVAudioQuality.high.rawValue
        ]
        
        print("🎤 AudioService: Audio settings configured:")
        print("   - Format: Linear PCM")
        print("   - Sample Rate: 16000 Hz")
        print("   - Channels: 1 (Mono)")
        print("   - Bit Depth: 16-bit")
        print("   - Quality: High")
        
        print("🎤 AudioService: Creating AVAudioRecorder...")
        audioRecorder = try AVAudioRecorder(url: recordingURL!, settings: settings)
        audioRecorder?.delegate = self
        audioRecorder?.isMeteringEnabled = true
        print("✅ AudioService: AVAudioRecorder created successfully")
        
        print("🎤 AudioService: Starting recording...")
        audioRecorder?.record()
        print("✅ AudioService: Recording started")
        
        isRecording = true
        print("✅ AudioService: Recording state set to true")
        
        // Start monitoring audio levels
        print("🎤 AudioService: Starting audio level monitoring...")
        startLevelMonitoring()
        print("✅ AudioService: Audio level monitoring started")
        
        print("✅ AudioService: ===== AUDIO RECORDING STARTED =====")
    }
    
    func stopRecording() async throws -> Data {
        print("🎤 AudioService: ===== STOPPING AUDIO RECORDING =====")
        print("🎤 AudioService: Timestamp: \(Date())")
        
        guard let recorder = audioRecorder, recorder.isRecording else {
            print("❌ AudioService: Not currently recording")
            throw AudioError.notRecording
        }
        
        print("✅ AudioService: Recording state verified")
        
        print("🎤 AudioService: Stopping recorder...")
        recorder.stop()
        print("✅ AudioService: Recorder stopped")
        
        isRecording = false
        print("✅ AudioService: Recording state set to false")
        
        print("🎤 AudioService: Stopping level monitoring...")
        stopLevelMonitoring()
        print("✅ AudioService: Level monitoring stopped")
        
        guard let url = recordingURL else {
            print("❌ AudioService: No recording URL available")
            throw AudioError.recordingFailed
        }
        
        print("🎤 AudioService: Reading audio file from: \(url.absoluteString)")
        
        // Read the audio file
        let audioData = try Data(contentsOf: url)
        print("✅ AudioService: Audio file read successfully")
        print("✅ AudioService: Audio data size: \(audioData.count) bytes")
        print("✅ AudioService: Audio data size in KB: \(Double(audioData.count) / 1024.0) KB")
        
        // Clean up the temporary file
        print("🎤 AudioService: Cleaning up temporary file...")
        try? FileManager.default.removeItem(at: url)
        print("✅ AudioService: Temporary file cleaned up")
        
        print("✅ AudioService: ===== AUDIO RECORDING STOPPED =====")
        return audioData
    }
    
    func playAudio(from urlString: String) async throws {
        guard let url = URL(string: urlString) else {
            throw AudioError.invalidURL
        }
        
        // Download the audio file
        let (data, _) = try await URLSession.shared.data(from: url)
        
        // Save to temporary file
        let tempURL = FileManager.default.temporaryDirectory.appendingPathComponent("audio-\(Date().timeIntervalSince1970).mp3")
        try data.write(to: tempURL)
        
        // Play the audio
        audioPlayer = try AVAudioPlayer(contentsOf: tempURL)
        audioPlayer?.delegate = self
        audioPlayer?.prepareToPlay()
        audioPlayer?.play()
        
        isPlaying = true
        
        // Clean up temp file after playback
        Task {
            try? await Task.sleep(nanoseconds: UInt64(5 * 1_000_000_000)) // Wait 5 seconds
            try? FileManager.default.removeItem(at: tempURL)
        }
    }
    
    func stopAudio() {
        audioPlayer?.stop()
        isPlaying = false
    }
    
    private func startLevelMonitoring() {
        levelTimer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            self?.updateAudioLevel()
        }
    }
    
    private func stopLevelMonitoring() {
        levelTimer?.invalidate()
        levelTimer = nil
        audioLevel = 0.0
    }
    
    private func updateAudioLevel() {
        guard let recorder = audioRecorder else { return }
        
        recorder.updateMeters()
        let level = recorder.averagePower(forChannel: 0)
        let normalizedLevel = pow(10, level / 20) // Convert dB to linear scale
        
        DispatchQueue.main.async {
            self.audioLevel = normalizedLevel
        }
    }
    
    enum AudioError: LocalizedError {
        case microphonePermissionDenied
        case notRecording
        case recordingFailed
        case invalidURL
        case playbackFailed
        
        var errorDescription: String? {
            switch self {
            case .microphonePermissionDenied:
                return "Microphone permission denied"
            case .notRecording:
                return "Not currently recording"
            case .recordingFailed:
                return "Recording failed"
            case .invalidURL:
                return "Invalid audio URL"
            case .playbackFailed:
                return "Audio playback failed"
            }
        }
    }
}

// MARK: - AVAudioRecorderDelegate
extension AudioService: AVAudioRecorderDelegate {
    func audioRecorderDidFinishRecording(_ recorder: AVAudioRecorder, successfully flag: Bool) {
        if !flag {
            print("[AudioService] Recording failed")
        }
    }
}

// MARK: - AVAudioPlayerDelegate
extension AudioService: AVAudioPlayerDelegate {
    func audioPlayerDidFinishPlaying(_ player: AVAudioPlayer, successfully flag: Bool) {
        DispatchQueue.main.async {
            self.isPlaying = false
        }
    }
    
    func audioPlayerDecodeErrorDidOccur(_ player: AVAudioPlayer, error: Error?) {
        print("[AudioService] Audio playback error: \(error?.localizedDescription ?? "Unknown error")")
        DispatchQueue.main.async {
            self.isPlaying = false
        }
    }
}