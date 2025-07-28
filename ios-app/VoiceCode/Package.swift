// swift-tools-version: 5.9
// The swift-tools-version declares the minimum version of Swift required to build this package.

import PackageDescription

let package = Package(
    name: "VoiceCode",
    platforms: [
        .iOS(.v16)
    ],
    products: [
        .library(
            name: "VoiceCode",
            targets: ["VoiceCode"]),
    ],
    dependencies: [
        // SignalR Client for Swift
        .package(url: "https://github.com/moozzyk/SignalR-Client-Swift", from: "0.9.0"),
        // Alamofire for networking
        .package(url: "https://github.com/Alamofire/Alamofire.git", from: "5.8.0"),
        // KeychainAccess for secure storage
        .package(url: "https://github.com/kishikawakatsumi/KeychainAccess.git", from: "4.2.0"),
    ],
    targets: [
        .target(
            name: "VoiceCode",
            dependencies: [
                .product(name: "SignalRClient", package: "SignalR-Client-Swift"),
                "Alamofire",
                "KeychainAccess"
            ],
            path: "Sources"),
        .testTarget(
            name: "VoiceCodeTests",
            dependencies: ["VoiceCode"],
            path: "Tests"),
    ]
)