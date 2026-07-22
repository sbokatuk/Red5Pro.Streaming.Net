using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Red5Pro.Streaming.Net.iOS;

// Bound against the @objc facade in native/ios/Facade/Red5ProFacade.swift, which is built into
// Red5ProFacade.xcframework by native/ios/fetch-ios.sh.
//
// Red5WebRTCKit itself exposes nothing to Objective-C at all - its framework ships with an empty
// Headers/ directory and exports no _OBJC_CLASS_$_ symbols - so there is no vendor API to bind
// directly. See docs/BUILD.md.
//
// Written by hand rather than generated with Objective Sharpie: the facade's surface is small and
// designed in this repository, so the selectors are known exactly. They are taken verbatim from
// the generated Red5ProFacade-Swift.h, which matters more than it looks - Swift's argument-label
// mangling turns setVideoSize(width:height:) into setVideoSizeWithWidth:height:, and a plausible
// guess would compile and then throw "unrecognized selector" at runtime.

/// <summary>
/// Callbacks raised by <see cref="R5Client" />.
///
/// Every member is optional. <see cref="R5ClientDelegate.OnLicenseValidated" /> is the one always
/// worth handling: Red5 validates the licence key before anything else happens, so a bad key
/// surfaces there rather than as a publish failure.
/// </summary>
[Protocol, Model]
[BaseType(typeof(NSObject), Name = "R5ClientDelegate")]
interface R5ClientDelegate
{
    /// <summary>
    /// The result of the licence check, raised shortly after <see cref="R5Client.Build" />.
    /// Nothing will stream when <paramref name="validated" /> is false.
    /// </summary>
    [Export("onLicenseValidated:message:")]
    void OnLicenseValidated(bool validated, string message);

    [Export("onPublishStarted")]
    void OnPublishStarted();

    [Export("onPublishStopped")]
    void OnPublishStopped();

    [Export("onPublishFailed:")]
    void OnPublishFailed(string error);

    [Export("onSubscribeStarted")]
    void OnSubscribeStarted();

    [Export("onSubscribeStopped")]
    void OnSubscribeStopped();

    [Export("onSubscribeFailed:")]
    void OnSubscribeFailed(string error);

    /// <summary>The local camera is running. Raised in response to <see cref="R5Client.StartPreview" />.</summary>
    [Export("onPreviewStarted")]
    void OnPreviewStarted();

    [Export("onPreviewStopped")]
    void OnPreviewStopped();

    [Export("onIceStateChanged:")]
    void OnIceStateChanged(R5IceState state);

    [Export("onPeerStateChanged:")]
    void OnPeerStateChanged(R5PeerState state);

    [Export("onError:")]
    void OnError(string error);
}

/// <summary>
/// Publishes to and subscribes from a Red5 Pro server or a Red5 Cloud stream manager.
///
/// Configure with the setters, call <see cref="Build" />, then publish or subscribe. The two
/// deployment shapes differ only in which host setter is used:
///
/// <code>
/// // Red5 Cloud
/// client.SetStreamManagerHost("your-id.cloud.red5.net");
/// client.SetPort(443);
/// client.SetNodeGroup("default");
///
/// // Red5 Pro, standalone server
/// client.SetServerIp("192.0.2.10");
/// client.SetPort(5080);
/// </code>
/// </summary>
[BaseType(typeof(NSObject), Name = "R5Client")]
interface R5Client
{
    /// <summary>
    /// Receives the callbacks. Held <b>weakly</b>, so keep your delegate in a field — one that
    /// only this assignment referenced would be collected and the callbacks would stop arriving
    /// with no error anywhere.
    /// </summary>
    [Wrap("WeakDelegate")]
    [NullAllowed]
    R5ClientDelegate Delegate { get; set; }

    [NullAllowed, Export("delegate", ArgumentSemantic.Weak)]
    NSObject WeakDelegate { get; set; }

    // Configuration. Applied to the builder, so they must be set before Build().

    /// <summary>Red5 Cloud: the stream manager host, e.g. <c>your-id.cloud.red5.net</c>.</summary>
    [Export("setStreamManagerHost:")]
    void SetStreamManagerHost(string host);

    /// <summary>Red5 Pro standalone: the server's host name or IP.</summary>
    [Export("setServerIp:")]
    void SetServerIp(string serverIp);

    /// <summary>443 for Red5 Cloud, 5080 for a standalone Red5 Pro server.</summary>
    [Export("setPort:")]
    void SetPort(nint port);

    /// <summary>The server application, <c>live</c> unless the deployment says otherwise.</summary>
    [Export("setAppName:")]
    void SetAppName(string appName);

    /// <summary>Red5 Cloud only; <c>default</c> unless you have created others.</summary>
    [Export("setNodeGroup:")]
    void SetNodeGroup(string nodeGroup);

    /// <summary>
    /// The <b>SDK</b> licence key. Red5 issues a separate server licence key which is not
    /// interchangeable with this one.
    /// </summary>
    [Export("setLicenseKey:")]
    void SetLicenseKey(string licenseKey);

    [Export("setStreamName:")]
    void SetStreamName(string streamName);

    [Export("setToken:")]
    void SetToken(string token);

    [Export("setVideoEnabled:")]
    void SetVideoEnabled(bool enabled);

    [Export("setAudioEnabled:")]
    void SetAudioEnabled(bool enabled);

    // Swift's argument labels are part of the selector; this is not setVideoSize:height:.
    [Export("setVideoSizeWithWidth:height:")]
    void SetVideoSize(nint width, nint height);

    [Export("setVideoFps:")]
    void SetVideoFps(nint fps);

    [Export("setVideoBitrate:")]
    void SetVideoBitrate(nint kbps);

    [Export("setVideoSource:")]
    void SetVideoSource(R5VideoSource source);

    /// <summary>
    /// Renders into <paramref name="view" />. The facade adds its own renderer as a subview
    /// pinned to the bounds, so an ordinary <see cref="UIView" /> is all that is needed — which
    /// is what a MAUI handler has.
    /// </summary>
    [Export("setVideoView:")]
    void SetVideoView(UIView view);

    [Export("setScalingType:")]
    void SetScalingType(R5ScalingType scalingType);

    /// <summary>
    /// Realises the configuration. This is what triggers licence validation, so
    /// <see cref="R5ClientDelegate.OnLicenseValidated" /> is normally the first callback seen.
    /// </summary>
    [Export("build")]
    void Build();

    [Export("startPreview")]
    void StartPreview();

    [Export("stopPreview")]
    void StopPreview();

    [Export("publish:")]
    void Publish(string streamName);

    [Export("stopPublish")]
    void StopPublish();

    [Export("subscribe:")]
    void Subscribe(string streamName);

    [Export("stopSubscribe")]
    void StopSubscribe();

    /// <summary>Ends the session and releases the camera, microphone and peer connection.</summary>
    [Export("stop")]
    void Stop();

    [Export("switchCamera")]
    void SwitchCamera();

    /// <summary>
    /// Mutes or unmutes the outgoing video track. This does not release the camera — the OS will
    /// still show the app as capturing.
    /// </summary>
    [Export("toggleSendVideo:")]
    void ToggleSendVideo(bool enabled);

    /// <inheritdoc cref="ToggleSendVideo" />
    [Export("toggleSendAudio:")]
    void ToggleSendAudio(bool enabled);

    [Export("changeCaptureFormatWithWidth:height:framerate:")]
    void ChangeCaptureFormat(nint width, nint height, nint framerate);

    [Export("changeVideoSource:")]
    void ChangeVideoSource(R5VideoSource source);

    /// <summary>Changes the bitrate of a running session, unlike <see cref="SetVideoBitrate" />.</summary>
    [Export("setStreamVideoBitrate:")]
    void SetStreamVideoBitrate(nint kbps);

    [Export("isPublishing")]
    bool IsPublishing();

    [Export("isSubscribing")]
    bool IsSubscribing();

    [Export("isReleased")]
    bool IsReleased();

    /// <summary>False until the server has confirmed the licence key.</summary>
    [Export("isLicenseValidated")]
    bool IsLicenseValidated();

    /// <summary>True once <see cref="Build" /> has run.</summary>
    [Export("isBuilt")]
    bool IsBuilt();

    /// <summary>
    /// The Red5WebRTCKit version actually loaded. Worth surfacing because the SDK is supplied by
    /// the consumer, so it need not be the version this binding was generated from.
    /// </summary>
    [Static]
    [Export("sdkVersion")]
    string SdkVersion();
}
