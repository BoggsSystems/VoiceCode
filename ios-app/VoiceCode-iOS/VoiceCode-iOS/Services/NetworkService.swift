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
        print("📡 NetworkService: ===== STARTING LOGIN REQUEST =====")
        print("📡 NetworkService: Timestamp: \(Date())")
        print("📡 NetworkService: Backend URL: \(baseURL)")
        
        // Use the simple-token endpoint like the web app
        guard let url = URL(string: "\(baseURL)/api/authentication/simple-token") else {
            print("❌ NetworkService: Invalid URL - cannot construct login endpoint")
            throw NetworkError.invalidURL
        }
        
        print("📡 NetworkService: Login endpoint URL: \(url.absoluteString)")
        print("📡 NetworkService: Username: \(username)")
        print("📡 NetworkService: Password length: \(password.count) characters")
        
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        
        // The simple-token endpoint might not need credentials, but send them anyway
        let body = ["username": username, "password": password]
        request.httpBody = try JSONEncoder().encode(body)
        
        print("📡 NetworkService: Request method: \(request.httpMethod ?? "Unknown")")
        print("📡 NetworkService: Request headers: \(request.allHTTPHeaderFields ?? [:])")
        print("📡 NetworkService: Request body size: \(request.httpBody?.count ?? 0) bytes")
        print("📡 NetworkService: Request body: \(String(data: request.httpBody ?? Data(), encoding: .utf8) ?? "nil")")
        
        print("📡 NetworkService: About to send HTTP request to backend...")
        
        do {
            let (data, response) = try await session.data(for: request)
            
            print("📡 NetworkService: ===== RESPONSE RECEIVED =====")
            print("📡 NetworkService: Timestamp: \(Date())")
            print("📡 NetworkService: Response data size: \(data.count) bytes")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ NetworkService: Invalid response type - not HTTP response")
                throw NetworkError.invalidResponse
            }
            
            print("📡 NetworkService: HTTP Status Code: \(httpResponse.statusCode)")
            print("📡 NetworkService: Response Headers: \(httpResponse.allHeaderFields)")
            
            if let responseString = String(data: data, encoding: .utf8) {
                print("📡 NetworkService: Response Body: \(responseString)")
            } else {
                print("📡 NetworkService: Response Body: Unable to decode as UTF-8")
            }
            
            switch httpResponse.statusCode {
            case 200:
                print("✅ NetworkService: ===== LOGIN SUCCESSFUL =====")
                print("✅ NetworkService: Backend returned 200 OK")
                
                var loginResponse = try JSONDecoder().decode(LoginResponse.self, from: data)
                print("✅ NetworkService: Successfully decoded login response")
                print("✅ NetworkService: Token: \(loginResponse.token.prefix(20))...")
                print("✅ NetworkService: Token length: \(loginResponse.token.count) characters")
                print("✅ NetworkService: Type: \(loginResponse.type ?? "Unknown")")
                print("✅ NetworkService: ExpiresIn: \(loginResponse.expiresIn ?? 0) seconds")
                
                print("✅ NetworkService: ===== LOGIN REQUEST COMPLETE =====")
                return loginResponse
                
            case 401:
                print("❌ NetworkService: ===== LOGIN FAILED - UNAUTHORIZED =====")
                print("❌ NetworkService: Backend returned 401 Unauthorized")
                print("❌ NetworkService: This indicates invalid credentials")
                throw NetworkError.unauthorized
                
            case 404:
                print("❌ NetworkService: ===== LOGIN FAILED - ENDPOINT NOT FOUND =====")
                print("❌ NetworkService: Backend returned 404 Not Found")
                print("❌ NetworkService: The login endpoint might not exist or be misconfigured")
                throw NetworkError.serverError("Login endpoint not found. The server might be using a different API structure.")
                
            default:
                print("❌ NetworkService: ===== LOGIN FAILED - SERVER ERROR =====")
                print("❌ NetworkService: Backend returned status code: \(httpResponse.statusCode)")
                
                if let errorData = try? JSONDecoder().decode([String: String].self, from: data),
                   let message = errorData["message"] ?? errorData["error"] {
                    print("❌ NetworkService: Server error message: \(message)")
                    throw NetworkError.serverError(message)
                } else {
                    print("❌ NetworkService: No detailed error message from server")
                    throw NetworkError.serverError("Server error: \(httpResponse.statusCode)")
                }
            }
        } catch {
            print("❌ NetworkService: ===== NETWORK ERROR =====")
            print("❌ NetworkService: Timestamp: \(Date())")
            print("❌ NetworkService: Error type: \(type(of: error))")
            print("❌ NetworkService: Network error: \(error)")
            print("❌ NetworkService: Error description: \(error.localizedDescription)")
            throw error
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