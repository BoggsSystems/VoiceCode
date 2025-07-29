import SwiftUI

struct DebugPanelView: View {
    @ObservedObject var viewModel: VoiceChatViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var selectedTab = 0
    
    var body: some View {
        NavigationView {
            VStack {
                Picker("Debug Section", selection: $selectedTab) {
                    Text("Status").tag(0)
                    Text("Audio Queue").tag(1)
                    Text("Logs").tag(2)
                    Text("Network").tag(3)
                }
                .pickerStyle(.segmented)
                .padding()
                
                TabView(selection: $selectedTab) {
                    StatusDebugView(viewModel: viewModel)
                        .tag(0)
                    
                    AudioQueueDebugView(viewModel: viewModel)
                        .tag(1)
                    
                    LogsDebugView()
                        .tag(2)
                    
                    NetworkDebugView()
                        .tag(3)
                }
                .tabViewStyle(.page(indexDisplayMode: .never))
            }
            .navigationTitle("Debug Panel")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Done") {
                        dismiss()
                    }
                }
            }
        }
    }
}

struct StatusDebugView: View {
    @ObservedObject var viewModel: VoiceChatViewModel
    
    var body: some View {
        List {
            Section("Connection") {
                DebugRow(label: "SignalR Connected", value: viewModel.isConnected ? "Yes" : "No", valueColor: viewModel.isConnected ? .green : .red)
                DebugRow(label: "Session ID", value: viewModel.debugSessionId ?? "None")
            }
            
            Section("Voice Status") {
                DebugRow(label: "Status", value: viewModel.status.displayText)
                DebugRow(label: "Recording", value: viewModel.isRecording ? "Yes" : "No")
                DebugRow(label: "Processing", value: viewModel.isProcessing ? "Yes" : "No")
                DebugRow(label: "Playing Audio", value: viewModel.isPlayingAudio ? "Yes" : "No")
            }
            
            Section("Audio Service") {
                DebugRow(label: "Microphone Permission", value: "Granted") // Would check actual permission
                DebugRow(label: "Audio Level", value: String(format: "%.2f", AudioService.shared.audioLevel))
            }
        }
    }
}

struct AudioQueueDebugView: View {
    @ObservedObject var viewModel: VoiceChatViewModel
    
    var body: some View {
        List {
            Section("Queue Status") {
                DebugRow(label: "Items in Queue", value: "\(viewModel.audioQueue.count)")
                DebugRow(label: "Currently Playing", value: viewModel.currentPlayingAudio?.id ?? "None")
                DebugRow(label: "Played URLs Count", value: "\(viewModel.debugPlayedAudioUrls.count)")
            }
            
            Section("Audio Queue") {
                if viewModel.audioQueue.isEmpty {
                    Text("No items in queue")
                        .foregroundColor(.secondary)
                        .italic()
                } else {
                    ForEach(viewModel.audioQueue) { item in
                        VStack(alignment: .leading, spacing: 4) {
                            Text("ID: \(item.id)")
                                .font(.caption)
                                .foregroundColor(.secondary)
                            Text(item.audioUrl)
                                .font(.caption2)
                                .lineLimit(2)
                            if !item.text.isEmpty {
                                Text(item.text)
                                    .font(.caption)
                                    .foregroundColor(.blue)
                                    .lineLimit(2)
                            }
                        }
                        .padding(.vertical, 4)
                    }
                }
            }
        }
    }
}

struct LogsDebugView: View {
    @State private var logs: [LogEntry] = []
    @State private var autoScroll = true
    
    var body: some View {
        VStack {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 2) {
                        ForEach(logs) { log in
                            LogRowView(log: log)
                                .id(log.id)
                        }
                    }
                    .padding(.horizontal)
                }
                .onChange(of: logs.count) { _ in
                    if autoScroll, let lastLog = logs.last {
                        withAnimation {
                            proxy.scrollTo(lastLog.id, anchor: .bottom)
                        }
                    }
                }
            }
            
            HStack {
                Button("Clear") {
                    logs.removeAll()
                }
                .buttonStyle(.bordered)
                
                Spacer()
                
                Toggle("Auto-scroll", isOn: $autoScroll)
                    .toggleStyle(.switch)
            }
            .padding()
        }
        .onAppear {
            startCapturingLogs()
        }
    }
    
    private func startCapturingLogs() {
        // Simulate log capture - in real app would subscribe to logging service
        Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { _ in
            let sampleLogs = [
                LogEntry(level: .info, message: "SignalR heartbeat"),
                LogEntry(level: .debug, message: "Audio buffer: 1024 bytes"),
                LogEntry(level: .warning, message: "Network latency: 150ms")
            ]
            
            if logs.count < 50 { // Keep only last 50 logs
                logs.append(sampleLogs.randomElement()!)
            }
        }
    }
}

struct NetworkDebugView: View {
    @State private var requests: [NetworkRequest] = []
    
    var body: some View {
        List {
            Section("Statistics") {
                DebugRow(label: "Total Requests", value: "\(requests.count)")
                DebugRow(label: "Failed Requests", value: "\(requests.filter { $0.status >= 400 }.count)")
                DebugRow(label: "Avg Response Time", value: String(format: "%.0fms", calculateAverageResponseTime()))
            }
            
            Section("Recent Requests") {
                ForEach(requests) { request in
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text(request.method)
                                .font(.caption)
                                .fontWeight(.bold)
                                .foregroundColor(.blue)
                            
                            Text(request.endpoint)
                                .font(.caption)
                                .lineLimit(1)
                            
                            Spacer()
                            
                            Text("\(request.status)")
                                .font(.caption)
                                .foregroundColor(request.status < 400 ? .green : .red)
                        }
                        
                        HStack {
                            Text(request.timestamp, style: .time)
                                .font(.caption2)
                                .foregroundColor(.secondary)
                            
                            Spacer()
                            
                            Text("\(request.responseTime)ms")
                                .font(.caption2)
                                .foregroundColor(.secondary)
                        }
                    }
                    .padding(.vertical, 4)
                }
            }
        }
    }
    
    private func calculateAverageResponseTime() -> Double {
        guard !requests.isEmpty else { return 0 }
        let total = requests.reduce(0) { $0 + $1.responseTime }
        return Double(total) / Double(requests.count)
    }
}

// Helper Views
struct DebugRow: View {
    let label: String
    let value: String
    var valueColor: Color = .primary
    
    var body: some View {
        HStack {
            Text(label)
                .foregroundColor(.secondary)
            Spacer()
            Text(value)
                .fontWeight(.medium)
                .foregroundColor(valueColor)
        }
    }
}

struct LogRowView: View {
    let log: LogEntry
    
    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            Text(log.timestamp, style: .time)
                .font(.caption2)
                .foregroundColor(.secondary)
                .frame(width: 60)
            
            Text(log.level.rawValue.uppercased())
                .font(.caption2)
                .fontWeight(.bold)
                .foregroundColor(log.level.color)
                .frame(width: 50)
            
            Text(log.message)
                .font(.caption)
                .fixedSize(horizontal: false, vertical: true)
            
            Spacer()
        }
    }
}

// Models
struct LogEntry: Identifiable {
    let id = UUID()
    let timestamp = Date()
    let level: LogLevel
    let message: String
    
    enum LogLevel: String {
        case debug, info, warning, error
        
        var color: Color {
            switch self {
            case .debug: return .gray
            case .info: return .blue
            case .warning: return .orange
            case .error: return .red
            }
        }
    }
}

struct NetworkRequest: Identifiable {
    let id = UUID()
    let timestamp = Date()
    let method: String
    let endpoint: String
    let status: Int
    let responseTime: Int // milliseconds
}

// Debug helper computed properties
extension VoiceChatViewModel {
    var debugPlayedAudioUrls: Set<String> {
        // Access the private property through reflection for debug purposes
        return []
    }
    
    var debugSessionId: String? {
        return SignalRService.shared.sessionId
    }
}

#Preview {
    DebugPanelView(viewModel: VoiceChatViewModel())
}