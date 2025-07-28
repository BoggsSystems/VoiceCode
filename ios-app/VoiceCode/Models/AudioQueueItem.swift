import Foundation

struct AudioQueueItem: Identifiable, Equatable {
    let id: String
    let audioUrl: String
    let text: String
    let timestamp: Date
    
    init(id: String = UUID().uuidString, audioUrl: String, text: String = "", timestamp: Date = Date()) {
        self.id = id
        self.audioUrl = audioUrl
        self.text = text
        self.timestamp = timestamp
    }
}