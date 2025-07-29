import SwiftUI

// Simple logger to capture console output
class DebugLogger: ObservableObject {
    static let shared = DebugLogger()
    @Published var logs: [LogEntry] = []
    
    struct LogEntry: Identifiable {
        let id = UUID()
        let timestamp = Date()
        let message: String
        let type: LogType
    }
    
    enum LogType {
        case info, success, error, network
        
        var icon: String {
            switch self {
            case .info: return "ℹ️"
            case .success: return "✅"
            case .error: return "❌"
            case .network: return "📡"
            }
        }
        
        var color: Color {
            switch self {
            case .info: return .primary
            case .success: return .green
            case .error: return .red
            case .network: return .blue
            }
        }
    }
    
    func log(_ message: String, type: LogType = .info) {
        DispatchQueue.main.async {
            self.logs.append(LogEntry(message: message, type: type))
            // Keep only last 100 logs
            if self.logs.count > 100 {
                self.logs.removeFirst()
            }
        }
    }
    
    func clear() {
        logs.removeAll()
    }
}

// Override print to capture logs
public func print(_ items: Any..., separator: String = " ", terminator: String = "\n") {
    let message = items.map { "\($0)" }.joined(separator: separator)
    
    // Determine log type based on content
    let logType: DebugLogger.LogType
    if message.contains("❌") {
        logType = .error
    } else if message.contains("✅") {
        logType = .success
    } else if message.contains("📡") {
        logType = .network
    } else {
        logType = .info
    }
    
    // Add to debug logger
    DebugLogger.shared.log(message, type: logType)
    
    // Also print to console
    Swift.print(message, terminator: terminator)
}

struct DebugConsoleView: View {
    @StateObject private var logger = DebugLogger.shared
    @State private var autoScroll = true
    
    var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Text("Debug Console")
                    .font(.headline)
                
                Spacer()
                
                Toggle("Auto Scroll", isOn: $autoScroll)
                    .toggleStyle(.switch)
                    .scaleEffect(0.8)
                
                Button(action: {
                    logger.clear()
                }) {
                    Label("Clear", systemImage: "trash")
                        .font(.caption)
                }
                .buttonStyle(.bordered)
            }
            .padding()
            .background(Color(UIColor.secondarySystemBackground))
            
            // Logs
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 4) {
                        ForEach(logger.logs) { log in
                            LogEntryView(entry: log)
                                .id(log.id)
                        }
                    }
                    .padding()
                }
                .onChange(of: logger.logs.count) { _ in
                    if autoScroll, let lastLog = logger.logs.last {
                        withAnimation {
                            proxy.scrollTo(lastLog.id, anchor: .bottom)
                        }
                    }
                }
            }
            .background(Color(UIColor.systemBackground))
        }
        .navigationTitle("Debug Console")
        .navigationBarTitleDisplayMode(.inline)
    }
}

struct LogEntryView: View {
    let entry: DebugLogger.LogEntry
    
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .top, spacing: 4) {
                Text(entry.type.icon)
                    .font(.caption)
                
                VStack(alignment: .leading, spacing: 2) {
                    Text(entry.timestamp, style: .time)
                        .font(.caption2)
                        .foregroundColor(.secondary)
                    
                    Text(entry.message)
                        .font(.system(.caption, design: .monospaced))
                        .foregroundColor(entry.type.color)
                        .textSelection(.enabled)
                }
                
                Spacer()
            }
            
            Divider()
        }
    }
}

#Preview {
    NavigationView {
        DebugConsoleView()
    }
}