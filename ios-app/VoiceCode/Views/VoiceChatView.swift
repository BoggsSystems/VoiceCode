import SwiftUI
import AVFoundation

struct VoiceChatView: View {
    @StateObject private var viewModel = VoiceChatViewModel()
    @EnvironmentObject var appState: AppState
    @State private var showingDebugPanel = false
    
    var body: some View {
        NavigationView {
            ZStack {
                // Background
                Color(.systemBackground)
                    .ignoresSafeArea()
                
                VStack(spacing: 20) {
                    // Status indicator
                    Text(viewModel.status.displayText)
                        .font(.headline)
                        .foregroundColor(statusColor)
                        .padding(.top, 20)
                    
                    Spacer()
                    
                    // Microphone button
                    MicrophoneButton(
                        isRecording: viewModel.isRecording,
                        isEnabled: viewModel.isConnected && !viewModel.isProcessing,
                        action: {
                            Task {
                                await viewModel.toggleRecording()
                            }
                        }
                    )
                    
                    Spacer()
                    
                    // Transcript and Response
                    VStack(spacing: 16) {
                        if !viewModel.currentTranscript.isEmpty {
                            TranscriptView(
                                title: "You said:",
                                text: viewModel.currentTranscript,
                                isUser: true
                            )
                            .transition(.move(edge: .bottom).combined(with: .opacity))
                        }
                        
                        if !viewModel.currentResponse.isEmpty {
                            TranscriptView(
                                title: "Response:",
                                text: viewModel.currentResponse,
                                isUser: false
                            )
                            .transition(.move(edge: .bottom).combined(with: .opacity))
                        }
                    }
                    .animation(.easeInOut, value: viewModel.currentTranscript)
                    .animation(.easeInOut, value: viewModel.currentResponse)
                    
                    Spacer()
                    
                    // Connection status
                    HStack {
                        Circle()
                            .fill(viewModel.isConnected ? Color.green : Color.red)
                            .frame(width: 8, height: 8)
                        Text(viewModel.isConnected ? "Connected" : "Connecting...")
                            .font(.caption)
                            .foregroundColor(.secondary)
                    }
                    .padding(.bottom, 20)
                }
                .padding(.horizontal)
                
                // Audio player overlay
                if viewModel.isPlayingAudio {
                    VStack {
                        Spacer()
                        AudioPlayerOverlay(
                            currentAudio: viewModel.currentPlayingAudio,
                            onSkip: {
                                viewModel.skipCurrentAudio()
                            }
                        )
                        .transition(.move(edge: .bottom))
                    }
                }
            }
            .navigationTitle("VoiceCode")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button(action: {
                        showingDebugPanel.toggle()
                    }) {
                        Image(systemName: "ladybug")
                    }
                }
            }
            .sheet(isPresented: $showingDebugPanel) {
                DebugPanelView(viewModel: viewModel)
            }
            .alert("Microphone Permission Required", isPresented: $viewModel.showPermissionAlert) {
                Button("Open Settings") {
                    if let url = URL(string: UIApplication.openSettingsURLString) {
                        UIApplication.shared.open(url)
                    }
                }
                Button("Cancel", role: .cancel) { }
            } message: {
                Text("Please allow microphone access in Settings to use voice chat.")
            }
            .onAppear {
                Task {
                    await viewModel.initialize()
                }
            }
        }
    }
    
    private var statusColor: Color {
        switch viewModel.status {
        case .idle:
            return .primary
        case .listening:
            return .blue
        case .processing:
            return .orange
        case .speaking:
            return .green
        case .error:
            return .red
        }
    }
}

struct MicrophoneButton: View {
    let isRecording: Bool
    let isEnabled: Bool
    let action: () -> Void
    
    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .fill(isRecording ? Color.red : Color.blue)
                    .frame(width: 120, height: 120)
                
                Image(systemName: "mic.fill")
                    .font(.system(size: 50))
                    .foregroundColor(.white)
                
                if isRecording {
                    Circle()
                        .stroke(Color.red.opacity(0.5), lineWidth: 4)
                        .scaleEffect(isRecording ? 1.5 : 1.0)
                        .opacity(isRecording ? 0.0 : 1.0)
                        .animation(.easeOut(duration: 1.0).repeatForever(autoreverses: false), value: isRecording)
                }
            }
        }
        .disabled(!isEnabled)
        .scaleEffect(isRecording ? 1.1 : 1.0)
        .animation(.easeInOut(duration: 0.2), value: isRecording)
    }
}

struct TranscriptView: View {
    let title: String
    let text: String
    let isUser: Bool
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(.caption)
                .foregroundColor(.secondary)
            
            Text(text)
                .font(.body)
                .padding()
                .background(isUser ? Color.blue.opacity(0.1) : Color.gray.opacity(0.1))
                .cornerRadius(12)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct AudioPlayerOverlay: View {
    let currentAudio: AudioQueueItem?
    let onSkip: () -> Void
    
    var body: some View {
        HStack {
            Image(systemName: "speaker.wave.2.fill")
                .foregroundColor(.blue)
            
            Text("Playing audio response...")
                .font(.caption)
            
            Spacer()
            
            Button(action: onSkip) {
                Image(systemName: "forward.fill")
                    .font(.caption)
            }
        }
        .padding()
        .background(Material.regular)
        .cornerRadius(12)
        .padding()
        .shadow(radius: 4)
    }
}

#Preview {
    VoiceChatView()
        .environmentObject(AppState())
}