import Foundation

enum VoiceStatus {
    case idle
    case listening
    case processing
    case speaking
    case error(String)
    
    var displayText: String {
        switch self {
        case .idle:
            return "Press to Talk"
        case .listening:
            return "Listening..."
        case .processing:
            return "Processing..."
        case .speaking:
            return "Speaking..."
        case .error(let message):
            return message
        }
    }
}