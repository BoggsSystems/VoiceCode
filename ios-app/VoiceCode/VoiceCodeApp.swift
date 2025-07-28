import SwiftUI

@main
struct VoiceCodeApp: App {
    @StateObject private var appState = AppState()
    
    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(appState)
                .preferredColorScheme(.light)
        }
    }
}

// Global app state
class AppState: ObservableObject {
    @Published var isAuthenticated = false
    @Published var authToken: String?
    @Published var currentUser: String?
    
    init() {
        // Check for existing auth token in keychain
        checkAuthStatus()
    }
    
    private func checkAuthStatus() {
        if let token = KeychainService.shared.getAuthToken(),
           let user = KeychainService.shared.getCurrentUser() {
            self.authToken = token
            self.currentUser = user
            self.isAuthenticated = true
        }
    }
    
    func login(token: String, user: String) {
        KeychainService.shared.saveAuthToken(token)
        KeychainService.shared.saveCurrentUser(user)
        self.authToken = token
        self.currentUser = user
        self.isAuthenticated = true
    }
    
    func logout() {
        KeychainService.shared.deleteAuthToken()
        KeychainService.shared.deleteCurrentUser()
        self.authToken = nil
        self.currentUser = nil
        self.isAuthenticated = false
    }
}