import SwiftUI

struct ContentView: View {
    @EnvironmentObject var appState: AppState
    
    var body: some View {
        if appState.isAuthenticated {
            MainTabView()
                .onAppear {
                    print("📲 ContentView: Showing MainTabView (authenticated)")
                }
        } else {
            LoginView()
                .onAppear {
                    print("🔐 ContentView: Showing LoginView (not authenticated)")
                }
        }
    }
}

struct MainTabView: View {
    var body: some View {
        TabView {
            VoiceChatView()
                .tabItem {
                    Label("Chat", systemImage: "mic.circle.fill")
                }
                .onAppear {
                    print("🎤 MainTabView: Voice Chat tab appeared")
                }
            
            SettingsView()
                .tabItem {
                    Label("Settings", systemImage: "gear")
                }
                .onAppear {
                    print("⚙️ MainTabView: Settings tab appeared")
                }
        }
        .onAppear {
            print("📱 MainTabView: Tab view initialized")
        }
    }
}

#Preview {
    ContentView()
        .environmentObject(AppState())
}