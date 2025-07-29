import Foundation
import Combine

@MainActor
class LoginViewModel: ObservableObject {
    @Published var username = "test@voicecode.dev"
    @Published var password = "TestPassword123!"
    @Published var isLoading = false
    @Published var errorMessage: String?
    @Published var isAuthenticated = false
    @Published var authToken: String?
    
    private let networkService = NetworkService.shared
    
    var isFormValid: Bool {
        !username.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
        !password.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
    
    func login() async {
        guard isFormValid else { 
            print("❌ LoginViewModel: Form is not valid")
            return 
        }
        
        print("🔐 LoginViewModel: ===== STARTING LOGIN PROCESS =====")
        print("🔐 LoginViewModel: Timestamp: \(Date())")
        print("🔐 LoginViewModel: Username: \(username)")
        print("🔐 LoginViewModel: Password length: \(password.count) characters")
        
        isLoading = true
        errorMessage = nil
        
        do {
            let trimmedUsername = username.trimmingCharacters(in: .whitespacesAndNewlines)
            print("🔐 LoginViewModel: Trimmed Username: \(trimmedUsername)")
            print("🔐 LoginViewModel: About to call NetworkService.login()")
            
            let response = try await networkService.login(
                username: trimmedUsername,
                password: password
            )
            
            print("✅ LoginViewModel: ===== LOGIN SUCCESSFUL =====")
            print("✅ LoginViewModel: Timestamp: \(Date())")
            print("✅ LoginViewModel: Token received: \(response.token.prefix(20))...")
            print("✅ LoginViewModel: Token length: \(response.token.count) characters")
            print("✅ LoginViewModel: Token type: \(response.type ?? "Unknown")")
            print("✅ LoginViewModel: Token expires in: \(response.expiresIn ?? 0) seconds")
            print("✅ LoginViewModel: User: \(trimmedUsername)")
            
            authToken = response.token
            isAuthenticated = true
            errorMessage = nil
            
            // Store the username separately since API doesn't return it
            print("🔐 LoginViewModel: Saving user credentials to Keychain")
            KeychainService.shared.saveCurrentUser(trimmedUsername)
            print("✅ LoginViewModel: User credentials saved successfully")
            
            print("✅ LoginViewModel: ===== LOGIN PROCESS COMPLETE =====")
        } catch {
            print("❌ LoginViewModel: ===== LOGIN FAILED =====")
            print("❌ LoginViewModel: Timestamp: \(Date())")
            print("❌ LoginViewModel: Error type: \(type(of: error))")
            print("❌ LoginViewModel: Error: \(error)")
            print("❌ LoginViewModel: Error Description: \(error.localizedDescription)")
            
            errorMessage = error.localizedDescription
            isAuthenticated = false
            authToken = nil
            
            print("❌ LoginViewModel: ===== LOGIN FAILURE COMPLETE =====")
        }
        
        isLoading = false
        print("🔐 LoginViewModel: Login process completed, isLoading set to false")
    }
}

// Login Response Model
struct LoginResponse: Codable {
    let token: String
    let type: String?
    let expiresIn: Int?
    
    // The API doesn't return a user field, so we'll handle that separately
    var user: String {
        // In a real app, you might decode the JWT to get the user
        // For now, we'll use the username that was used to login
        return ""
    }
}