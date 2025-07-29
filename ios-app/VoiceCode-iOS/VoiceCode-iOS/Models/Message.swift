import Foundation

struct Message: Identifiable, Equatable {
    let id: String
    let content: String
    let type: MessageType
    let timestamp: Date
    
    enum MessageType {
        case user
        case assistant
        case system
    }
    
    init(id: String = UUID().uuidString, content: String, type: MessageType, timestamp: Date = Date()) {
        self.id = id
        self.content = content
        self.type = type
        self.timestamp = timestamp
    }
}