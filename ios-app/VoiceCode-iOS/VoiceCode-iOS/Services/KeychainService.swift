import Foundation
import Security

class KeychainService {
    static let shared = KeychainService()
    
    private let authTokenKey = "com.voicecode.authToken"
    private let userKey = "com.voicecode.currentUser"
    
    private init() {}
    
    func saveAuthToken(_ token: String) {
        print("🔐 KeychainService: Saving auth token")
        save(token, for: authTokenKey)
    }
    
    func getAuthToken() -> String? {
        print("🔍 KeychainService: Retrieving auth token")
        let token = get(for: authTokenKey)
        if token != nil {
            print("✅ KeychainService: Auth token found")
        } else {
            print("❌ KeychainService: No auth token found")
        }
        return token
    }
    
    func deleteAuthToken() {
        print("🗑️ KeychainService: Deleting auth token")
        delete(for: authTokenKey)
    }
    
    func saveCurrentUser(_ user: String) {
        print("👤 KeychainService: Saving current user: \(user)")
        save(user, for: userKey)
    }
    
    func getCurrentUser() -> String? {
        print("🔍 KeychainService: Retrieving current user")
        let user = get(for: userKey)
        if let user = user {
            print("✅ KeychainService: Current user found: \(user)")
        } else {
            print("❌ KeychainService: No current user found")
        }
        return user
    }
    
    func deleteCurrentUser() {
        print("🗑️ KeychainService: Deleting current user")
        delete(for: userKey)
    }
    
    private func save(_ value: String, for key: String) {
        let data = value.data(using: .utf8)!
        
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: key,
            kSecValueData as String: data
        ]
        
        SecItemDelete(query as CFDictionary)
        SecItemAdd(query as CFDictionary, nil)
    }
    
    private func get(for key: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true
        ]
        
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        
        guard status == errSecSuccess,
              let data = result as? Data,
              let string = String(data: data, encoding: .utf8) else {
            return nil
        }
        
        return string
    }
    
    private func delete(for key: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: key
        ]
        
        SecItemDelete(query as CFDictionary)
    }
}