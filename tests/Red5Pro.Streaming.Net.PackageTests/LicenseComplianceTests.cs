namespace Red5Pro.Streaming.Net.PackageTests;

/// <summary>
/// Asserts that no packed package contains the Red5 SDK.
///
/// This is the most important file in the test suite, and the only one whose failure is a legal
/// problem rather than a bug. The Infrared5 EULA defines "Software" to include SDKs (1.11) and then
/// forbids repackaging (3.6) and bundling or distributing them "in any manner whatsoever" (3.7).
/// The binding projects therefore build *against* the SDK and exclude it from the package.
///
/// That exclusion is fragile in a way that is easy to miss: Pack="false" on an AndroidLibrary item
/// is silently ignored, and the default behaviour puts the .aar into every lib/&lt;tfm&gt;/. It was
/// exactly that default which shipped the SDK on the first attempt here, and nothing but a test
/// like this would have caught it before a push to nuget.org.
/// </summary>
public class LicenseComplianceTests
{
    /// <summary>
    /// File-name fragments that identify a Red5-distributed binary. Deliberately broad: a future
    /// SDK release renaming its artifact should trip this test rather than slip past it.
    /// </summary>
    private static readonly string[] Red5SdkMarkers =
    [
        "red5-android-sdk",
        "Red5WebRTCKit",
        "red5streaming",
        "R5Streaming",
    ];

    public static IEnumerable<object[]> AllPackages =>
        new[] { Packages.Android, Packages.IOS, Packages.Core, Packages.Maui }
            .Select(id => new object[] { id });

    [SkippableTheory]
    [MemberData(nameof(AllPackages))]
    public void No_package_contains_the_red5_sdk(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} was not built on this runner");

        using var package = Packages.OpenPackage(packageId);

        var offenders = package.Entries
            .Where(entry => Red5SdkMarkers.Any(marker =>
                entry.FullName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.FullName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{packageId} contains Red5 SDK binaries, which the Red5 EULA does not permit " +
            $"redistributing:\n  {string.Join("\n  ", offenders)}\n\n" +
            "The binding builds against the SDK but must exclude it from the package; consumers " +
            "supply their own licensed copy via Red5ProAndroidSdk / Red5ProIosSdk.");
    }

    [SkippableFact]
    public void Android_package_ships_libwebrtc_exactly_once()
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var package = Packages.OpenPackage(Packages.Android);

        var copies = package.Entries
            .Where(e => e.FullName.EndsWith("webrtc-android.aar", StringComparison.Ordinal))
            .Select(e => e.FullName)
            .ToList();

        // libwebrtc is BSD and *may* ship - unlike the Red5 SDK - but it is 47 MB, so a copy per
        // target framework triples the package for no benefit. It is packed once at the root and
        // wired in by build/*.targets.
        Assert.True(
            copies.Count == 1,
            $"expected exactly one copy of libwebrtc, found {copies.Count}: " +
            $"{string.Join(", ", copies)}");

        Assert.StartsWith("native/", copies[0], StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Ios_package_ships_only_ios_slices_of_libwebrtc()
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.IOS);

        // stasel's xcframework carries macos and maccatalyst slices too, at ~25 MB each. This
        // package is iOS-only - Red5 publish no Catalyst slice for their own SDK, so a Catalyst
        // build could never link - and native/ios/fetch-ios.sh strips them.
        var unwanted = package.Entries
            .Where(e => e.FullName.Contains("WebRTC.xcframework/", StringComparison.Ordinal))
            .Where(e => e.FullName.Contains("maccatalyst", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.Contains("macos-", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();

        Assert.True(
            unwanted.Count == 0,
            $"the iOS package carries non-iOS libwebrtc slices ({unwanted.Count} entries). " +
            "native/ios/fetch-ios.sh should have stripped them.");
    }

    [SkippableTheory]
    [MemberData(nameof(AllPackages))]
    public void Packages_declare_the_expected_nuspec_metadata(string packageId)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} was not built on this runner");

        using var package = Packages.OpenPackage(packageId);
        var nuspec = Packages.ReadNuspec(package, packageId);

        string Value(string name) => nuspec.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

        Assert.Equal(packageId, Value("id"));
        Assert.NotEmpty(Value("version"));

        // MIT describes the binding and client code, which is all these packages actually contain.
        Assert.Equal("MIT", Value("license"));
        Assert.NotEmpty(Value("description"));
        Assert.Equal("icon.png", Value("icon"));
        Assert.Equal("README.md", Value("readme"));

        // Packed from the files the icon/readme metadata points at, so a rename that broke the
        // packaging would otherwise only show up on nuget.org.
        Assert.True(package.GetEntry("icon.png") is not null, $"{packageId} declares an icon it does not ship.");
        Assert.True(package.GetEntry("README.md") is not null, $"{packageId} declares a readme it does not ship.");
    }
}
