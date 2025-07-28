import Foundation
import Combine

@MainActor
class LoginViewModel: ObservableObject {
    @Published var username = ""
    @Published var password = ""
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
        guard isFormValid else { return }
        
        isLoading = true
        errorMessage = nil
        
        do {
            let response = try await networkService.login(
                username: username.trimmingCharacters(in: .whitespacesAndNewlines),
                password: password
            )
            
            authToken = response.token
            isAuthenticated = true
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
            isAuthenticated = false
            authToken = nil
        }
        
        isLoading = false
    }
}

// Login Response Model
struct LoginResponse: Codable {
    let token: String
    let user: String
    let expiresIn: Int?
}