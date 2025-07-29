// swift-tools-version: 5.9
// The swift-tools-version declares the minimum version of Swift required to build this package.

import PackageDescription

let package = Package(
    name: "VoiceCode-iOS",
    platforms: [
        .iOS(.v16)
    ],
    dependencies: [
        .package(url: "https://github.com/moozzyk/SignalR-Client-Swift", from: "0.9.2")
    ],
    targets: [
        .target(
            name: "VoiceCode-iOS",
            dependencies: [
                .product(name: "SignalRClient", package: "SignalR-Client-Swift")
            ]
        )
    ]
)