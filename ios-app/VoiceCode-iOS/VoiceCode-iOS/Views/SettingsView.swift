import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var appState: AppState
    @State private var showingLogoutAlert = false
    @AppStorage("debugModeEnabled") private var debugModeEnabled = false
    @AppStorage("audioOutputPreference") private var audioOutputPreference = "automatic"
    
    var body: some View {
        NavigationView {
            Form {
                Section("Account") {
                    HStack {
                        Text("Logged in as")
                            .foregroundColor(.secondary)
                        Spacer()
                        Text(appState.currentUser ?? "Unknown")
                            .fontWeight(.medium)
                    }
                    
                    Button(action: {
                        showingLogoutAlert = true
                    }) {
                        HStack {
                            Text("Logout")
                                .foregroundColor(.red)
                            Spacer()
                            Image(systemName: "arrow.right.square")
                                .foregroundColor(.red)
                        }
                    }
                }
                
                Section("Audio") {
                    Picker("Audio Output", selection: $audioOutputPreference) {
                        Text("Automatic").tag("automatic")
                        Text("Speaker").tag("speaker")
                        Text("Bluetooth").tag("bluetooth")
                    }
                    
                    NavigationLink(destination: AudioSettingsDetailView()) {
                        HStack {
                            Text("Advanced Audio Settings")
                            Spacer()
                            Image(systemName: "chevron.right")
                                .foregroundColor(.secondary)
                                .font(.caption)
                        }
                    }
                }
                
                Section("Developer") {
                    Toggle("Debug Mode", isOn: $debugModeEnabled)
                    
                    if debugModeEnabled {
                        NavigationLink(destination: DebugConsoleView()) {
                            HStack {
                                Text("Debug Console")
                                Spacer()
                                Image(systemName: "terminal")
                                    .foregroundColor(.secondary)
                            }
                        }
                        
                        NavigationLink(destination: DebugSettingsView()) {
                            HStack {
                                Text("Debug Settings")
                                Spacer()
                                Image(systemName: "chevron.right")
                                    .foregroundColor(.secondary)
                                    .font(.caption)
                            }
                        }
                        
                        Button(action: clearCache) {
                            HStack {
                                Text("Clear Cache")
                                Spacer()
                                Image(systemName: "trash")
                                    .foregroundColor(.secondary)
                            }
                        }
                    }
                }
                
                Section("About") {
                    HStack {
                        Text("Version")
                            .foregroundColor(.secondary)
                        Spacer()
                        Text("1.0.0")
                    }
                    
                    HStack {
                        Text("Build")
                            .foregroundColor(.secondary)
                        Spacer()
                        Text("1")
                    }
                    
                    NavigationLink(destination: AboutView()) {
                        HStack {
                            Text("About VoiceCode")
                            Spacer()
                            Image(systemName: "chevron.right")
                                .foregroundColor(.secondary)
                                .font(.caption)
                        }
                    }
                }
            }
            .navigationTitle("Settings")
            .alert("Logout", isPresented: $showingLogoutAlert) {
                Button("Cancel", role: .cancel) { }
                Button("Logout", role: .destructive) {
                    logout()
                }
            } message: {
                Text("Are you sure you want to logout?")
            }
        }
    }
    
    private func logout() {
        // Disconnect SignalR
        Task {
            await SignalRService.shared.disconnect()
        }
        
        // Clear app state
        appState.logout()
    }
    
    private func clearCache() {
        // Clear audio cache
        let cacheURL = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first!
        try? FileManager.default.removeItem(at: cacheURL)
        
        // Clear temporary files
        let tempURL = FileManager.default.temporaryDirectory
        try? FileManager.default.removeItem(at: tempURL)
    }
}

struct AudioSettingsDetailView: View {
    @AppStorage("audioQuality") private var audioQuality = "high"
    @AppStorage("echoCancellation") private var echoCancellation = true
    @AppStorage("noiseSuppression") private var noiseSuppression = true
    
    var body: some View {
        Form {
            Section("Recording") {
                Picker("Audio Quality", selection: $audioQuality) {
                    Text("Low").tag("low")
                    Text("Medium").tag("medium")
                    Text("High").tag("high")
                }
                
                Toggle("Echo Cancellation", isOn: $echoCancellation)
                Toggle("Noise Suppression", isOn: $noiseSuppression)
            }
            
            Section("Playback") {
                // Additional playback settings
            }
        }
        .navigationTitle("Audio Settings")
        .navigationBarTitleDisplayMode(.inline)
    }
}

struct DebugSettingsView: View {
    @AppStorage("showNetworkLogs") private var showNetworkLogs = false
    @AppStorage("showSignalRLogs") private var showSignalRLogs = true
    @AppStorage("verboseLogging") private var verboseLogging = false
    
    var body: some View {
        Form {
            Section("Logging") {
                Toggle("Show Network Logs", isOn: $showNetworkLogs)
                Toggle("Show SignalR Logs", isOn: $showSignalRLogs)
                Toggle("Verbose Logging", isOn: $verboseLogging)
            }
            
            Section("Actions") {
                Button("Export Logs") {
                    exportLogs()
                }
                
                Button("Send Test Audio") {
                    sendTestAudio()
                }
            }
        }
        .navigationTitle("Debug Settings")
        .navigationBarTitleDisplayMode(.inline)
    }
    
    private func exportLogs() {
        // Export logs implementation
    }
    
    private func sendTestAudio() {
        // Send test audio implementation
    }
}

struct AboutView: View {
    var body: some View {
        VStack(spacing: 20) {
            Image(systemName: "mic.circle.fill")
                .font(.system(size: 100))
                .foregroundColor(.blue)
                .padding(.top, 40)
            
            Text("VoiceCode")
                .font(.largeTitle)
                .fontWeight(.bold)
            
            Text("Voice-Powered AI Assistant")
                .font(.headline)
                .foregroundColor(.secondary)
            
            VStack(alignment: .leading, spacing: 16) {
                InfoRow(title: "Developer", value: "VoiceCode Team")
                InfoRow(title: "Website", value: "voicecode.app")
                InfoRow(title: "Support", value: "support@voicecode.app")
            }
            .padding(.horizontal, 40)
            .padding(.top, 40)
            
            Spacer()
            
            Text("© 2025 VoiceCode. All rights reserved.")
                .font(.caption)
                .foregroundColor(.secondary)
                .padding(.bottom)
        }
        .navigationTitle("About")
        .navigationBarTitleDisplayMode(.inline)
    }
}

struct InfoRow: View {
    let title: String
    let value: String
    
    var body: some View {
        HStack {
            Text(title)
                .foregroundColor(.secondary)
            Spacer()
            Text(value)
                .fontWeight(.medium)
        }
    }
}

#Preview {
    SettingsView()
        .environmentObject(AppState())
}