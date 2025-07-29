//
//  VoiceCode_iOSApp.swift
//  VoiceCode-iOS
//
//  Created by Jeff Boggs on 2025-07-28.
//

import SwiftUI

@main
struct VoiceCode_iOSApp: App {
    @StateObject private var appState = AppState()
    
    init() {
        print("🚀 VoiceCode iOS App: Launching...")
        print("📱 VoiceCode iOS App: Initializing app structure")
    }
    
    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(appState)
                .preferredColorScheme(.light)
                .onAppear {
                    print("🪟 VoiceCode iOS App: Main window appeared")
                }
        }
    }
}

// Global app state
class AppState: ObservableObject {
    @Published var isAuthenticated = false
    @Published var authToken: String?
    @Published var currentUser: String?
    
    init() {
        print("🔐 AppState: Initializing app state")
        // Check for existing auth token in keychain
        checkAuthStatus()
    }
    
    private func checkAuthStatus() {
        print("🔍 AppState: Checking authentication status...")
        if let token = KeychainService.shared.getAuthToken(),
           let user = KeychainService.shared.getCurrentUser() {
            print("✅ AppState: Found existing auth token for user: \(user)")
            print("🔑 AppState: Token prefix: \(String(token.prefix(10)))...")
            self.authToken = token
            self.currentUser = user
            self.isAuthenticated = true
        } else {
            print("❌ AppState: No existing authentication found")
        }
    }
    
    func login(token: String, user: String) {
        print("🔓 AppState: Logging in user: \(user)")
        KeychainService.shared.saveAuthToken(token)
        KeychainService.shared.saveCurrentUser(user)
        self.authToken = token
        self.currentUser = user
        self.isAuthenticated = true
        print("✅ AppState: Login successful")
    }
    
    func logout() {
        print("🔒 AppState: Logging out user: \(currentUser ?? "unknown")")
        KeychainService.shared.deleteAuthToken()
        KeychainService.shared.deleteCurrentUser()
        self.authToken = nil
        self.currentUser = nil
        self.isAuthenticated = false
        print("✅ AppState: Logout complete")
    }
}
