import Foundation

class NetworkService {
    static let shared = NetworkService()
    
    private let baseURL = "https://voicecode-router.orangewater-a2f689a8.eastus.azurecontainerapps.io"
    private let session = URLSession.shared
    
    private init() {}
    
    enum NetworkError: LocalizedError {
        case invalidURL
        case noData
        case invalidResponse
        case unauthorized
        case serverError(String)
        
        var errorDescription: String? {
            switch self {
            case .invalidURL:
                return "Invalid URL"
            case .noData:
                return "No data received"
            case .invalidResponse:
                return "Invalid response from server"
            case .unauthorized:
                return "Invalid username or password"
            case .serverError(let message):
                return message
            }
        }
    }
    
    func login(username: String, password: String) async throws -> LoginResponse {
        guard let url = URL(string: "\(baseURL)/api/auth/login/simple") else {
            throw NetworkError.invalidURL
        }
        
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        
        let body = ["username": username, "password": password]
        request.httpBody = try JSONEncoder().encode(body)
        
        let (data, response) = try await session.data(for: request)
        
        guard let httpResponse = response as? HTTPURLResponse else {
            throw NetworkError.invalidResponse
        }
        
        switch httpResponse.statusCode {
        case 200:
            let loginResponse = try JSONDecoder().decode(LoginResponse.self, from: data)
            return loginResponse
        case 401:
            throw NetworkError.unauthorized
        default:
            if let errorData = try? JSONDecoder().decode([String: String].self, from: data),
               let message = errorData["message"] {
                throw NetworkError.serverError(message)
            } else {
                throw NetworkError.serverError("Server error: \(httpResponse.statusCode)")
            }
        }
    }
    
    func makeAuthenticatedRequest(to endpoint: String, method: String = "GET", body: Data? = nil) async throws -> Data {
        guard let token = KeychainService.shared.getAuthToken() else {
            throw NetworkError.unauthorized
        }
        
        guard let url = URL(string: "\(baseURL)\(endpoint)") else {
            throw NetworkError.invalidURL
        }
        
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        
        let (data, response) = try await session.data(for: request)
        
        guard let httpResponse = response as? HTTPURLResponse else {
            throw NetworkError.invalidResponse
        }
        
        guard httpResponse.statusCode == 200 else {
            if httpResponse.statusCode == 401 {
                throw NetworkError.unauthorized
            }
            throw NetworkError.serverError("Server error: \(httpResponse.statusCode)")
        }
        
        return data
    }
}