import Foundation

// MARK: - Transcription Models

struct TranscriptionResponse: Codable {
    let id: String?
    let text: String?
    let transcript: String? // Some responses use "transcript" instead of "text"
    let confidence: Double?
    let language: String?
    let words: [Word]?
    let audioUrl: String?
    let requestedAt: String?
    let userId: String?
    
    struct Word: Codable {
        let text: String
        let offset: Double
        let duration: Double
        let confidence: Double
    }
}

// MARK: - Voice Command Models

struct VoiceCommandRequest: Codable {
    let transcription: String
    let sessionId: String
    let timestamp: String
    let metadata: [String: String]
}

struct VoiceCommandResponse: Codable {
    let success: Bool
    let taskId: String
    let worker: Int
    let instructions: String
    let response: String
    let metadata: [String: AnyCodable]?
}

// MARK: - Audio Polling Models

struct AudioPollResponse: Codable {
    let taskId: String
    let status: String  // "pending" or "ready"
    let message: String?
    let audioUrl: String?
    let text: String?
    let duration: Double?
    let timestamp: Date?
}

// MARK: - Helper for Encoding/Decoding Any Type

struct AnyCodable: Codable {
    let value: Any
    
    init(_ value: Any) {
        self.value = value
    }
    
    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        
        if let bool = try? container.decode(Bool.self) {
            value = bool
        } else if let int = try? container.decode(Int.self) {
            value = int
        } else if let double = try? container.decode(Double.self) {
            value = double
        } else if let string = try? container.decode(String.self) {
            value = string
        } else if let array = try? container.decode([AnyCodable].self) {
            value = array.map { $0.value }
        } else if let dictionary = try? container.decode([String: AnyCodable].self) {
            value = dictionary.mapValues { $0.value }
        } else {
            value = NSNull()
        }
    }
    
    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        
        switch value {
        case let bool as Bool:
            try container.encode(bool)
        case let int as Int:
            try container.encode(int)
        case let double as Double:
            try container.encode(double)
        case let string as String:
            try container.encode(string)
        case let array as [Any]:
            try container.encode(array.map { AnyCodable($0) })
        case let dictionary as [String: Any]:
            try container.encode(dictionary.mapValues { AnyCodable($0) })
        default:
            let context = EncodingError.Context(codingPath: encoder.codingPath, debugDescription: "AnyCodable value cannot be encoded")
            throw EncodingError.invalidValue(value, context)
        }
    }
}