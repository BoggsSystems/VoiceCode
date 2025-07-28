import Foundation
import Security

class KeychainService {
    static let shared = KeychainService()
    
    private let authTokenKey = "com.voicecode.authToken"
    private let userKey = "com.voicecode.currentUser"
    
    private init() {}
    
    func saveAuthToken(_ token: String) {
        save(token, for: authTokenKey)
    }
    
    func getAuthToken() -> String? {
        return get(for: authTokenKey)
    }
    
    func deleteAuthToken() {
        delete(for: authTokenKey)
    }
    
    func saveCurrentUser(_ user: String) {
        save(user, for: userKey)
    }
    
    func getCurrentUser() -> String? {
        return get(for: userKey)
    }
    
    func deleteCurrentUser() {
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