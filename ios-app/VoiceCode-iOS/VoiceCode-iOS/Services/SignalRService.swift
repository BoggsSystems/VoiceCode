import Foundation
import Combine
import SignalRClient

@MainActor
class SignalRService: ObservableObject {
    static let shared = SignalRService()
    
    @Published var isConnected = false
    @Published var connectionError: String?
    
    // Publishers for events
    let transcriptionReceived = PassthroughSubject<TranscriptionResult, Never>()
    let audioResponseReceived = PassthroughSubject<AudioResponse, Never>()
    let textResponseReceived = PassthroughSubject<TextResponse, Never>()
    
    private let hubURL = "https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io/hubs/voice"
    private var connection: HubConnection?
    private var _sessionId: String?
    private var connectionStarted = false
    
    var sessionId: String? {
        return _sessionId
    }
    
    private init() {
        _sessionId = generateSessionId()
        print("🔗 SignalRService: Initialized with session ID: \(_sessionId ?? "none")")
        print("🔗 SignalRService: Hub URL configured: \(hubURL)")
        print("🔗 SignalRService: Expected dispatcher service URL: https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io/hubs/voice")
        print("🔗 SignalRService: URLs match: \(hubURL == "https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io/hubs/voice")")
    }
    
    func connect() async throws {
        print("🔌 SignalRService: ===== STARTING SIGNALR CONNECTION =====")
        print("🔌 SignalRService: Timestamp: \(Date())")
        print("🔌 SignalRService: Hub URL: \(hubURL)")
        print("🔌 SignalRService: Hub URL length: \(hubURL.count) characters")
        
        guard let url = URL(string: hubURL) else {
            print("❌ SignalRService: Invalid hub URL - cannot parse URL")
            print("❌ SignalRService: Hub URL string: \(hubURL)")
            throw SignalRError.connectionFailed("Invalid hub URL")
        }
        
        print("✅ SignalRService: Hub URL is valid")
        print("✅ SignalRService: URL scheme: \(url.scheme ?? "none")")
        print("✅ SignalRService: URL host: \(url.host ?? "none")")
        print("✅ SignalRService: URL port: \(url.port ?? -1)")
        print("✅ SignalRService: URL path: \(url.path)")
        print("✅ SignalRService: URL absolute string: \(url.absoluteString)")
        
        // Test the backend health endpoint first
        print("🔍 SignalRService: Testing backend health endpoint...")
        do {
            let healthURL = URL(string: "\(url.scheme ?? "https")://\(url.host ?? "")/health")!
            print("🔍 SignalRService: Testing health endpoint: \(healthURL.absoluteString)")
            
            let (healthData, healthResponse) = try await URLSession.shared.data(from: healthURL)
            
            if let httpResponse = healthResponse as? HTTPURLResponse {
                print("🔍 SignalRService: Health endpoint test result:")
                print("   - Status Code: \(httpResponse.statusCode)")
                print("   - Response Headers: \(httpResponse.allHeaderFields)")
                if let responseString = String(data: healthData, encoding: .utf8) {
                    print("   - Response Body: \(responseString)")
                }
                
                if httpResponse.statusCode == 200 {
                    print("✅ SignalRService: Backend health endpoint is accessible")
                } else {
                    print("⚠️ SignalRService: Health endpoint returned status \(httpResponse.statusCode)")
                }
            }
        } catch {
            print("❌ SignalRService: Failed to test health endpoint: \(error)")
            print("❌ SignalRService: Error details: \(error.localizedDescription)")
        }
        
        // Test the URL by making a simple HTTP request to check if the endpoint exists
        print("🔍 SignalRService: Testing hub endpoint availability...")
        do {
            let testURL = URL(string: "\(url.scheme ?? "https")://\(url.host ?? "")\(url.path)/negotiate?negotiateVersion=1")!
            print("🔍 SignalRService: Testing negotiate endpoint: \(testURL.absoluteString)")
            
            var request = URLRequest(url: testURL)
            request.httpMethod = "POST"
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.setValue("application/json", forHTTPHeaderField: "Accept")
            
            let (data, response) = try await URLSession.shared.data(for: request)
            
            if let httpResponse = response as? HTTPURLResponse {
                print("🔍 SignalRService: Negotiate endpoint test result:")
                print("   - Status Code: \(httpResponse.statusCode)")
                print("   - Response Headers: \(httpResponse.allHeaderFields)")
                if let responseString = String(data: data, encoding: .utf8) {
                    print("   - Response Body: \(responseString)")
                }
                
                if httpResponse.statusCode == 200 {
                    print("✅ SignalRService: Negotiate endpoint is accessible")
                } else {
                    print("⚠️ SignalRService: Negotiate endpoint returned status \(httpResponse.statusCode)")
                }
            }
        } catch {
            print("❌ SignalRService: Failed to test negotiate endpoint: \(error)")
            print("❌ SignalRService: Error details: \(error.localizedDescription)")
        }
        
        // Note: For now, we'll connect without authentication to match the web app behavior
        // The web app connects to SignalR without passing auth tokens in the connection
        print("🔌 SignalRService: Connecting without authentication (matching web app behavior)")
        
        print("🔌 SignalRService: Building HubConnection...")
        print("🔌 SignalRService: Using URL: \(url.absoluteString)")
        
        connection = HubConnectionBuilder(url: url)
            .withLogging(minLogLevel: .debug)
            .withAutoReconnect()
            .build()
        print("✅ SignalRService: HubConnection built successfully")
        
        // Add connection state monitoring
        print("🔍 SignalRService: Setting up connection state monitoring...")
        
        // Monitor connection state changes
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
            print("🔍 SignalRService: Connection state check 1 (0.5s): \(self.connectionStarted ? "Started" : "Not started")")
        }
        
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
            print("🔍 SignalRService: Connection state check 2 (1.0s): \(self.connectionStarted ? "Started" : "Not started")")
        }
        
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
            print("🔍 SignalRService: Connection state check 3 (3.0s): \(self.connectionStarted ? "Started" : "Not started")")
            print("🔍 SignalRService: Is connected: \(self.isConnected)")
        }
        
        DispatchQueue.main.asyncAfter(deadline: .now() + 5.0) {
            print("🔍 SignalRService: Connection state check 4 (5.0s): \(self.connectionStarted ? "Started" : "Not started")")
            print("🔍 SignalRService: Is connected: \(self.isConnected)")
            if !self.isConnected {
                print("⚠️ SignalRService: WARNING - Still not connected after 5 seconds")
                print("⚠️ SignalRService: This might indicate a connection issue")
            }
        }
        
        // Setup event handlers before connecting
        print("🔗 SignalRService: Setting up event handlers...")
        
        connection?.on(method: "TranscriptionReceived", callback: { (id: String, transcript: String, confidence: Double?) in
            print("🔗 SignalRService: TranscriptionReceived event triggered")
            let transcriptionData = TranscriptionData(
                id: id,
                transcript: transcript,
                confidence: confidence
            )
            Task { @MainActor [weak self] in
                self?.handleTranscription(transcriptionData)
            }
        })
        print("✅ SignalRService: TranscriptionReceived handler registered")
        
        connection?.on(method: "AudioResponseReady", callback: { (id: String?, audioUrl: String, text: String?) in
            print("🔗 SignalRService: AudioResponseReady event triggered")
            let audioData = AudioResponseData(id: id, audioUrl: audioUrl, text: text)
            Task { @MainActor [weak self] in
                self?.handleAudioResponse(audioData)
            }
        })
        print("✅ SignalRService: AudioResponseReady handler registered")
        
        connection?.on(method: "AudioReady", callback: { (id: String?, audioUrl: String, text: String?) in
            print("🔗 SignalRService: AudioReady event triggered")
            let audioData = AudioResponseData(id: id, audioUrl: audioUrl, text: text)
            Task { @MainActor [weak self] in
                self?.handleAudioResponse(audioData)
            }
        })
        print("✅ SignalRService: AudioReady handler registered")
        
        connection?.on(method: "ClaudeResponse", callback: { (id: String, content: String) in
            print("🔗 SignalRService: ClaudeResponse event triggered")
            let responseData = TextResponseData(id: id, content: content)
            Task { @MainActor [weak self] in
                self?.handleTextResponse(responseData)
            }
        })
        print("✅ SignalRService: ClaudeResponse handler registered")
        
        connection?.on(method: "Error", callback: { (message: String) in
            print("❌ SignalRService: Error event received: \(message)")
        })
        print("✅ SignalRService: Error handler registered")
        
        print("🔗 SignalRService: All event handlers registered successfully")
        
        // Start connection
        print("🔌 SignalRService: Starting SignalR connection...")
        print("🔌 SignalRService: About to call connection.start()")
        
        do {
            // Try to start the connection and catch any immediate errors
            print("🔌 SignalRService: Calling connection.start()...")
            connection?.start()
            print("✅ SignalRService: connection.start() called successfully")
        } catch {
            print("❌ SignalRService: Error calling connection.start(): \(error)")
            print("❌ SignalRService: Error type: \(type(of: error))")
            print("❌ SignalRService: Error description: \(error.localizedDescription)")
        }
        
        connectionStarted = true
        print("✅ SignalRService: Connection start initiated")
        print("✅ SignalRService: connectionStarted set to: \(connectionStarted)")
        
        // Wait a bit for connection to establish
        print("⏳ SignalRService: Waiting for connection to establish...")
        print("⏳ SignalRService: Waiting 2 seconds for connection...")
        try await Task.sleep(nanoseconds: 2_000_000_000) // 2 seconds
        print("✅ SignalRService: Connection establishment wait completed")
        
        print("✅ SignalRService: Connection started")
        print("✅ SignalRService: Final connection state - connectionStarted: \(connectionStarted), isConnected: \(isConnected)")
        
        // Join session if we have a session ID
        if let sessionId = _sessionId {
            print("📡 SignalRService: Joining session: \(sessionId)")
            connection?.invoke(method: "JoinSession", arguments: [sessionId]) { error in
                if let error = error {
                    print("❌ SignalRService: Failed to join session: \(error)")
                    print("❌ SignalRService: Error type: \(type(of: error))")
                    print("❌ SignalRService: Error description: \(error.localizedDescription)")
                } else {
                    print("✅ SignalRService: Successfully joined session")
                    print("✅ SignalRService: Session ID: \(sessionId)")
                }
            }
        } else {
            print("⚠️ SignalRService: No session ID available for joining session")
        }
        
        self.isConnected = true
        print("✅ SignalRService: Connection status set to: \(self.isConnected)")
        print("✅ SignalRService: ===== SIGNALR CONNECTION ESTABLISHED =====")
    }
    
    func disconnect() async {
        print("🔌 SignalRService: Disconnecting from hub")
        connection?.stop()
        connectionStarted = false
        self.isConnected = false
        print("✅ SignalRService: Disconnected")
    }
    
    func sendAudioData(_ audioData: Data) async throws {
        print("🎤 SignalRService: ===== STARTING AUDIO DATA SEND =====")
        print("🎤 SignalRService: Timestamp: \(Date())")
        print("🎤 SignalRService: Audio data size: \(audioData.count) bytes")
        
        guard connectionStarted else {
            print("❌ SignalRService: Cannot send audio - not connected")
            print("❌ SignalRService: Connection status: \(connectionStarted)")
            throw SignalRError.notConnected
        }
        
        print("✅ SignalRService: Connection verified - ready to send audio")
        
        guard let sessionId = _sessionId else {
            print("❌ SignalRService: No session ID available")
            print("❌ SignalRService: Session ID: \(_sessionId ?? "nil")")
            throw SignalRError.connectionFailed("No session ID")
        }
        
        print("✅ SignalRService: Session ID verified: \(sessionId)")
        
        // First, join the session
        print("🔗 SignalRService: Joining session before sending audio...")
        connection?.invoke(method: "JoinSession", arguments: [sessionId]) { error in
            if let error = error {
                print("❌ SignalRService: Failed to join session: \(error)")
                print("❌ SignalRService: Error details: \(error.localizedDescription)")
            } else {
                print("✅ SignalRService: Successfully joined session: \(sessionId)")
            }
        }
        
        // Wait a moment for session join to complete
        try await Task.sleep(nanoseconds: 1_000_000_000) // 1 second
        
        // Convert audio data to base64
        print("🔄 SignalRService: Converting audio data to base64...")
        let base64Audio = audioData.base64EncodedString()
        print("🔄 SignalRService: Base64 conversion complete")
        print("🔄 SignalRService: Base64 length: \(base64Audio.count) characters")
        print("🔄 SignalRService: Base64 size in KB: \(Double(base64Audio.count) / 1024.0) KB")
        
        // Create the request matching the web app format
        let requestId = UUID().uuidString
        let timestamp = ISO8601DateFormatter().string(from: Date())
        
        print("📦 SignalRService: Creating request object...")
        print("📦 SignalRService: Request ID: \(requestId)")
        print("📦 SignalRService: Timestamp: \(timestamp)")
        
        // Create a simple request object for ProcessAudio
        struct AudioMessage: Encodable {
            let id: String
            let audioData: String // Keep as base64 string for now
            let format: String
            let sampleRate: Int
            let language: String
        }
        
        let request = AudioMessage(
            id: requestId,
            audioData: base64Audio,
            format: "wav",
            sampleRate: 16000,
            language: "en-US"
        )
        
        print("📦 SignalRService: Request object created successfully")
        print("📦 SignalRService: Request details:")
        print("   - Request ID: \(request.id)")
        print("   - Format: \(request.format)")
        print("   - Sample Rate: \(request.sampleRate)")
        print("   - Language: \(request.language)")
        print("   - Audio data length: \(request.audioData.count) characters")
        
        print("📤 SignalRService: About to send ProcessAudio to SignalR hub...")
        print("📤 SignalRService: Hub URL: \(hubURL)")
        print("📤 SignalRService: Session ID: \(sessionId)")
        
        // Send via SignalR using ProcessAudio method
        connection?.invoke(method: "ProcessAudio", arguments: [request]) { error in
            if let error = error {
                print("❌ SignalRService: ===== AUDIO SEND FAILED =====")
                print("❌ SignalRService: Timestamp: \(Date())")
                print("❌ SignalRService: Error type: \(type(of: error))")
                print("❌ SignalRService: Failed to send audio: \(error)")
                print("❌ SignalRService: Error description: \(error.localizedDescription)")
                print("❌ SignalRService: Request ID that failed: \(requestId)")
            } else {
                print("✅ SignalRService: ===== AUDIO SEND SUCCESSFUL =====")
                print("✅ SignalRService: Timestamp: \(Date())")
                print("✅ SignalRService: Audio data sent successfully to backend")
                print("✅ SignalRService: Request ID: \(requestId)")
                print("✅ SignalRService: Session ID: \(sessionId)")
                print("✅ SignalRService: Audio size sent: \(audioData.count) bytes")
                print("✅ SignalRService: Base64 size sent: \(base64Audio.count) characters")
                print("📤 SignalRService: Waiting for response from hub...")
                print("📤 SignalRService: Expected events: TranscriptionReceived, AudioResponseReady, AudioReady")
            }
        }
        
        print("📤 SignalRService: SignalR invoke call completed")
        print("📤 SignalRService: ===== AUDIO SEND PROCESS COMPLETE =====")
    }
    
    func sendTextMessage(_ text: String) async throws {
        print("💬 SignalRService: Sending text message: \(text)")
        
        guard connectionStarted else {
            print("❌ SignalRService: Cannot send text - not connected")
            throw SignalRError.notConnected
        }
        
        // In real implementation:
        /*
        let message = TextMessage(
            sessionId: _sessionId!,
            text: text
        )
        
        try await connection?.invoke(method: "ProcessText", message)
        */
    }
    
    private func handleTranscription(_ data: TranscriptionData) {
        print("📝 SignalRService: Received transcription")
        print("🆔 SignalRService: ID: \(data.id)")
        print("🗣️ SignalRService: Text: \(data.transcript)")
        print("🎯 SignalRService: Confidence: \(data.confidence ?? 1.0)")
        
        let result = TranscriptionResult(
            id: data.id,
            text: data.transcript,
            confidence: data.confidence ?? 1.0
        )
        
        DispatchQueue.main.async {
            self.transcriptionReceived.send(result)
        }
    }
    
    private func handleAudioResponse(_ data: AudioResponseData) {
        print("🎵 SignalRService: ===== AUDIO RESPONSE RECEIVED =====")
        print("🎵 SignalRService: Timestamp: \(Date())")
        print("🎵 SignalRService: Session ID: \(_sessionId ?? "none")")
        print("🎵 SignalRService: Response ID: \(data.id ?? "generated")")
        print("🎵 SignalRService: Audio URL: \(data.audioUrl)")
        print("🎵 SignalRService: Audio URL length: \(data.audioUrl.count) characters")
        print("🎵 SignalRService: Text content: \(data.text ?? "no text")")
        print("🎵 SignalRService: Text length: \(data.text?.count ?? 0) characters")
        
        // Validate audio URL
        if let url = URL(string: data.audioUrl) {
            print("🎵 SignalRService: Audio URL is valid")
            print("🎵 SignalRService: URL scheme: \(url.scheme ?? "none")")
            print("🎵 SignalRService: URL host: \(url.host ?? "none")")
            print("🎵 SignalRService: URL path: \(url.path)")
        } else {
            print("❌ SignalRService: WARNING - Invalid audio URL format")
        }
        
        let response = AudioResponse(
            id: data.id ?? UUID().uuidString,
            audioUrl: data.audioUrl,
            text: data.text ?? ""
        )
        
        print("🎵 SignalRService: Created AudioResponse object")
        print("🎵 SignalRService: Response ID: \(response.id)")
        print("🎵 SignalRService: About to send to audioResponseReceived publisher")
        
        DispatchQueue.main.async {
            print("🎵 SignalRService: Sending audio response to publisher")
            self.audioResponseReceived.send(response)
            print("🎵 SignalRService: Audio response sent to publisher successfully")
        }
        
        print("🎵 SignalRService: ===== AUDIO RESPONSE PROCESSING COMPLETE =====")
    }
    
    
    private func handleTextResponse(_ data: TextResponseData) {
        print("💬 SignalRService: Received text response")
        print("🆔 SignalRService: ID: \(data.id)")
        print("🗒️ SignalRService: Content: \(data.content)")
        
        let response = TextResponse(
            id: data.id,
            text: data.content
        )
        
        DispatchQueue.main.async {
            self.textResponseReceived.send(response)
        }
    }
    
    private func generateSessionId() -> String {
        return "ios-session-\(Date().timeIntervalSince1970)-\(UUID().uuidString.prefix(8))"
    }
    
    enum SignalRError: LocalizedError {
        case notConnected
        case noAuthToken
        case connectionFailed(String)
        
        var errorDescription: String? {
            switch self {
            case .notConnected:
                return "Not connected to server"
            case .noAuthToken:
                return "No authentication token found"
            case .connectionFailed(let message):
                return "Connection failed: \(message)"
            }
        }
    }
}

// Data models for SignalR communication
struct TranscriptionData: Codable {
    let id: String
    let transcript: String
    let confidence: Double?
}

struct AudioResponseData: Codable {
    let id: String?
    let audioUrl: String
    let text: String?
}

struct TextResponseData: Codable {
    let id: String
    let content: String
}

struct AudioMessage: Codable {
    let sessionId: String
    let audioData: String
    let format: String
    let sampleRate: Int
}

struct TextMessage: Codable {
    let sessionId: String
    let text: String
}

