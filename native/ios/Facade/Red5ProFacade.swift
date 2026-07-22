//
//  Red5ProFacade.swift
//
//  An @objc surface over Red5WebRTCKit, so that .NET for iOS has something to bind.
//
//  WHY THIS EXISTS
//  ---------------
//  Red5WebRTCKit is pure Swift and exports nothing at all to Objective-C. Verified against
//  2.1.0.2:
//
//      Headers/                     empty - no generated Red5WebRTCKit-Swift.h
//      nm -gU Red5WebRTCKit         zero _OBJC_CLASS_$_ symbols
//      file Red5WebRTCKit           current ar archive random library  (static)
//
//  Objective Sharpie therefore sees nothing, and a binding project would generate an empty
//  assembly. Everything a caller needs - Red5WebrtcClientBuilder, the Red5WebrtcClient protocol,
//  Red5ProWebrtcEventDelegate, and the Swift enums - is invisible across the ABI.
//
//  WHY IT IS A SEPARATE FRAMEWORK
//  ------------------------------
//  The equivalent facade in the Ant Media bindings is compiled *into* the vendor's framework,
//  which is fine because that SDK is MIT. The Red5 EULA forbids it: section 3.4 (no derivative
//  works) and 3.6 (no repackaging of the Software "or any of its component parts"). So this is
//  built as its own framework that *links against* Red5WebRTCKit rather than absorbing it, which
//  is no different from what any consuming app does.
//
//  The practical consequence is that Red5ProFacade.framework carries undefined references to the
//  Red5 symbols, resolved when the consumer's app links their own licensed copy. That is exactly
//  what makes the BYO-SDK packaging work: our framework ships, theirs does not.
//
//  NAMING IS LOAD-BEARING
//  ----------------------
//  Every type carries an explicit @objc(R5...) name. Without one, Swift exports a class under its
//  mangled name (_OBJC_CLASS_$__TtC13Red5ProFacade8R5Client) while the .NET binding links against
//  _OBJC_CLASS_$_R5Client, and every consuming app dies at link time with an undefined symbol.
//  native/ios/fetch-ios.sh checks the built binary with nm and fails if the mangled form comes
//  back.
//

import AVFoundation
import Foundation
import Red5WebRTCKit
import UIKit
import WebRTC

// MARK: - Enums

/// Whether a session publishes or subscribes. Mirrors Red5WebRTCKit's Swift-only `ClientMode`.
@objc(R5Mode)
public enum R5Mode: Int {
    case publish = 0
    case subscribe = 1
}

/// Which camera, or screen, feeds the publisher. Mirrors `StreamSource`.
@objc(R5VideoSource)
public enum R5VideoSource: Int {
    case frontCamera = 0
    case rearCamera = 1
    case screen = 2
    case custom = 3

    fileprivate var streamSource: StreamSource {
        switch self {
        case .frontCamera: return .frontCamera
        case .rearCamera: return .rearCamera
        case .screen: return .screen
        case .custom: return .custom
        }
    }
}

/// ICE transport state. Mirrors `IceConnectionState`, flattened to Int for Objective-C.
///
/// `unknown` exists because Red5's enum is non-frozen: a future SDK can add a case, and without a
/// fallback this mapping would stop compiling (a hard error under Swift 6) or, worse, trap. A
/// consumer that sees `unknown` is running a newer SDK than the binding was generated from -
/// which is a real possibility here, since the SDK is supplied by the consumer, not by us.
@objc(R5IceState)
public enum R5IceState: Int {
    case unknown = -1
    case new = 0
    case checking = 1
    case connected = 2
    case completed = 3
    case failed = 4
    case disconnected = 5
    case closed = 6

    fileprivate init(_ state: IceConnectionState) {
        switch state {
        case .new: self = .new
        case .checking: self = .checking
        case .connected: self = .connected
        case .completed: self = .completed
        case .failed: self = .failed
        case .disconnected: self = .disconnected
        case .closed: self = .closed
        @unknown default: self = .unknown
        }
    }
}

/// Peer connection state. Mirrors `PeerConnectionState`. See ``R5IceState`` for why `unknown`
/// exists.
@objc(R5PeerState)
public enum R5PeerState: Int {
    case unknown = -1
    case new = 0
    case connecting = 1
    case connected = 2
    case disconnected = 3
    case failed = 4
    case closed = 5

    fileprivate init(_ state: PeerConnectionState) {
        switch state {
        case .new: self = .new
        case .connecting: self = .connecting
        case .connected: self = .connected
        case .disconnected: self = .disconnected
        case .failed: self = .failed
        case .closed: self = .closed
        @unknown default: self = .unknown
        }
    }
}

/// How video is fitted into its view. Mirrors `VideoScalingType`.
@objc(R5ScalingType)
public enum R5ScalingType: Int {
    case aspectFit = 0
    case aspectFill = 1
    case fill = 2
}

// MARK: - Delegate

/// Callbacks raised by ``R5Client``.
///
/// Every member is optional, so a .NET consumer overrides only what it needs. `onLicenseValidated`
/// is the one worth always handling: Red5 validates the licence key before anything else happens,
/// and a bad key surfaces there rather than as a publish failure.
@objc(R5ClientDelegate)
public protocol R5ClientDelegate: AnyObject {
    @objc optional func onLicenseValidated(_ validated: Bool, message: String)

    @objc optional func onPublishStarted()
    @objc optional func onPublishStopped()
    @objc optional func onPublishFailed(_ error: String)

    @objc optional func onSubscribeStarted()
    @objc optional func onSubscribeStopped()
    @objc optional func onSubscribeFailed(_ error: String)

    @objc optional func onPreviewStarted()
    @objc optional func onPreviewStopped()

    @objc optional func onIceStateChanged(_ state: R5IceState)
    @objc optional func onPeerStateChanged(_ state: R5PeerState)

    @objc optional func onError(_ error: String)
}

// MARK: - Client

/// Publishes to and subscribes from a Red5 Pro server or a Red5 Cloud stream manager.
///
/// Configuration is applied with setters and then realised by ``build()``, rather than exposing
/// Red5's fluent builder directly: a builder returning `Self` binds poorly into C#, and every
/// setter here would have to be duplicated on the builder anyway.
///
/// The two deployment shapes differ only in which host setter is used:
///
///     Red5 Pro (standalone)   setServerIp("1.2.3.4")            + setPort(5080)
///     Red5 Cloud              setStreamManagerHost("x.cloud…")  + setPort(443) + setNodeGroup
///
@objc(R5Client)
public class R5Client: NSObject {

    /// Held weakly, matching what Red5WebRTCKit does with its own event listener. A .NET caller
    /// must therefore keep its delegate alive in a field; the binding's documentation says so.
    @objc public weak var delegate: R5ClientDelegate?

    private var client: (any Red5WebrtcClient)?
    private let builder = Red5WebrtcClientBuilder()

    /// The renderer handed to the SDK. Created inside the view passed to ``setVideoView(_:)`` so
    /// that a caller only has to supply an ordinary `UIView` - which is all a MAUI handler has.
    private var renderer: RTCMTLVideoView?

    /// Retained because Red5WebRTCKit's `eventListener` is also a weak reference: a bridge that
    /// only the assignment referenced would be collected and the callbacks would silently stop.
    private var listener: EventBridge?

    @objc public override init() {
        super.init()

        let bridge = EventBridge(owner: self)
        listener = bridge
        builder.setEventListener(bridge)
    }

    // MARK: Configuration

    /// Red5 Cloud: the stream manager host, e.g. `userId-1234-abcd.cloud.red5.net`.
    @objc public func setStreamManagerHost(_ host: String) { builder.setStreamManagerHost(host) }

    /// Red5 Pro standalone: the server's host name or IP.
    @objc public func setServerIp(_ serverIp: String) { builder.setServerIp(serverIp) }

    /// 443 for Red5 Cloud, 5080 for a standalone Red5 Pro server.
    @objc public func setPort(_ port: Int) { builder.setPort(port) }

    /// The server application, `live` unless the deployment says otherwise.
    @objc public func setAppName(_ appName: String) { builder.setAppName(appName) }

    /// Red5 Cloud only; `default` unless you have created others.
    @objc public func setNodeGroup(_ nodeGroup: String) { builder.setNodeGroup(nodeGroup) }

    /// The **SDK** licence key, not the server licence key - Red5 issues two and they are not
    /// interchangeable. Validated before anything streams; see `onLicenseValidated`.
    @objc public func setLicenseKey(_ licenseKey: String) { builder.setLicenseKey(licenseKey) }

    @objc public func setStreamName(_ streamName: String) { builder.setStreamName(streamName) }
    @objc public func setToken(_ token: String) { builder.setToken(token) }

    @objc public func setVideoEnabled(_ enabled: Bool) { builder.setVideoEnabled(enabled) }
    @objc public func setAudioEnabled(_ enabled: Bool) { builder.setAudioEnabled(enabled) }

    @objc public func setVideoSize(width: Int, height: Int) {
        builder.setVideoWidth(width)
        builder.setVideoHeight(height)
    }

    @objc public func setVideoFps(_ fps: Int) { builder.setVideoFps(fps) }
    @objc public func setVideoBitrate(_ kbps: Int) { builder.setVideoBitrate(kbps) }

    @objc public func setVideoSource(_ source: R5VideoSource) {
        builder.setVideoSource(source.streamSource)
    }

    /// Renders into `view` by adding an `RTCMTLVideoView` as a subview, pinned to its bounds.
    ///
    /// Deliberately takes a plain `UIView`: the alternative is exposing `RTCVideoRenderer`, which
    /// would put a WebRTC type in the binding's public API and force every consumer to reference
    /// the WebRTC binding just to show a picture.
    @objc public func setVideoView(_ view: UIView) {
        let videoView = RTCMTLVideoView(frame: view.bounds)
        videoView.videoContentMode = .scaleAspectFill
        videoView.translatesAutoresizingMaskIntoConstraints = false

        view.addSubview(videoView)
        NSLayoutConstraint.activate([
            videoView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            videoView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            videoView.topAnchor.constraint(equalTo: view.topAnchor),
            videoView.bottomAnchor.constraint(equalTo: view.bottomAnchor),
        ])

        renderer = videoView
        builder.setVideoRenderer(videoView)
        client?.setVideoRenderer(videoView)
    }

    @objc public func setScalingType(_ scalingType: R5ScalingType) {
        switch scalingType {
        case .aspectFit: renderer?.videoContentMode = .scaleAspectFit
        case .aspectFill, .fill: renderer?.videoContentMode = .scaleAspectFill
        }
    }

    // MARK: Lifecycle

    /// Realises the configuration. Building is what triggers licence validation, so
    /// `onLicenseValidated` is normally the first callback a caller sees.
    @objc public func build() {
        client = builder.build()
    }

    @objc public func startPreview() { client?.startPreview() }
    @objc public func stopPreview() { client?.stopPreview() }

    @objc public func publish(_ streamName: String) { client?.publish(streamName: streamName) }
    @objc public func stopPublish() { client?.stopPublish() }

    @objc public func subscribe(_ streamName: String) { client?.subscribe(streamName: streamName) }
    @objc public func stopSubscribe() { client?.stopSubscribe() }

    @objc public func stop() { client?.stop() }

    // MARK: In-session controls

    @objc public func switchCamera() { client?.switchCamera() }
    @objc public func toggleSendVideo(_ enabled: Bool) { client?.toggleSendVideo(enabled) }
    @objc public func toggleSendAudio(_ enabled: Bool) { client?.toggleSendAudio(enabled) }

    @objc public func changeCaptureFormat(width: Int, height: Int, framerate: Int) {
        client?.changeCaptureFormat(width: width, height: height, framerate: framerate)
    }

    @objc public func changeVideoSource(_ source: R5VideoSource) {
        client?.changeVideoSource(source.streamSource)
    }

    @objc public func setStreamVideoBitrate(_ kbps: Int) { client?.setVideoBitrate(kbps) }

    // MARK: State

    @objc public func isPublishing() -> Bool { client?.isPublishing() ?? false }
    @objc public func isSubscribing() -> Bool { client?.isSubscribing() ?? false }
    @objc public func isReleased() -> Bool { client?.isReleased() ?? true }
    @objc public func isLicenseValidated() -> Bool { client?.isLicenseValidated() ?? false }
    @objc public func isBuilt() -> Bool { client != nil }

    /// The Red5WebRTCKit version this facade is running against, for diagnostics. Worth surfacing
    /// because the SDK is BYO: the version a consumer supplies need not be the one the binding was
    /// generated from.
    @objc public class func sdkVersion() -> String { Red5WebrtcClientConfig.getVersion() }

    // MARK: - Event bridge

    /// Adapts Red5WebRTCKit's Swift-only delegate to the @objc one above.
    ///
    /// A separate object rather than conforming `R5Client` itself, because `Red5ProWebrtcEventDelegate`
    /// is a Swift protocol: making the @objc class conform to it directly would drag Swift-only
    /// requirements into the type the binding is generated from.
    private final class EventBridge: Red5ProWebrtcEventDelegate {
        private weak var owner: R5Client?

        init(owner: R5Client) { self.owner = owner }

        private var target: R5ClientDelegate? { owner?.delegate }

        func onLicenseValidated(validated: Bool, message: String) {
            target?.onLicenseValidated?(validated, message: message)
        }

        func onPublishStarted() { target?.onPublishStarted?() }
        func onPublishStopped() { target?.onPublishStopped?() }
        func onPublishFailed(error: String) { target?.onPublishFailed?(error) }

        func onSubscribeStarted() { target?.onSubscribeStarted?() }
        func onSubscribeStopped() { target?.onSubscribeStopped?() }
        func onSubscribeFailed(error: String) { target?.onSubscribeFailed?(error) }

        func onPreviewStarted() { target?.onPreviewStarted?() }
        func onPreviewStopped() { target?.onPreviewStopped?() }

        func onIceConnectionStateChanged(state: IceConnectionState) {
            target?.onIceStateChanged?(R5IceState(state))
        }

        func onConnectionStateChanged(state: PeerConnectionState) {
            target?.onPeerStateChanged?(R5PeerState(state))
        }

        func onError(error: String) { target?.onError?(error) }
    }
}
