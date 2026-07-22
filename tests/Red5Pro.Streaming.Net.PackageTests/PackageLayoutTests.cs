namespace Red5Pro.Streaming.Net.PackageTests;

/// <summary>
/// Asserts the shape of the produced NuGet packages. These run against the packed .nupkg rather
/// than the build output, so they catch packaging regressions the compiler cannot see — most
/// importantly a target framework going missing because one of the two SDK-band passes failed or
/// build/merge-packages.py dropped it.
/// </summary>
public class PackageLayoutTests
{
    [SkippableTheory]
    [MemberData(nameof(Packages.AndroidFrameworks), MemberType = typeof(Packages))]
    public void Android_package_carries_a_binding_assembly_for_every_target_framework(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var package = Packages.OpenPackage(Packages.Android);

        var expected = $"lib/{tfm}/Red5Pro.Streaming.Net.Android.dll";
        Assert.True(
            package.GetEntry(expected) is not null,
            $"{Packages.Android} is missing '{expected}'.");
    }

    [SkippableTheory]
    [MemberData(nameof(Packages.IosFrameworks), MemberType = typeof(Packages))]
    public void Ios_package_carries_a_binding_assembly_for_every_target_framework(string tfm)
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.IOS);

        var expected = $"lib/{tfm}/Red5Pro.Streaming.Net.iOS.dll";
        Assert.True(
            package.GetEntry(expected) is not null,
            $"{Packages.IOS} is missing '{expected}'.");
    }

    [SkippableFact]
    public void Android_package_ships_a_usable_libwebrtc()
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var package = Packages.OpenPackage(Packages.Android);

        var aar = package.GetEntry("native/webrtc-android.aar");
        Assert.True(aar is not null, $"{Packages.Android} is missing native/webrtc-android.aar.");

        // The Red5 .aar carries no jniLibs at all, so every org.webrtc type and every native .so
        // comes from here. Anything small means a placeholder or a truncated download - which
        // would install fine and then die with UnsatisfiedLinkError on first use.
        Assert.True(
            aar!.Length > 20_000_000,
            $"'{aar.FullName}' is only {aar.Length} bytes; libwebrtc looks truncated.");
    }

    [SkippableFact]
    public void Ios_package_ships_both_frameworks_for_device_and_simulator()
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.IOS);

        // The frameworks travel in the binding's sidecar resources, which .NET for iOS extracts and
        // links during the consuming app's build. Device *and* simulator slices both have to be
        // there, or the package works in exactly one of the two places a developer will try it.
        foreach (var framework in new[] { "Red5ProFacade", "WebRTC" })
        {
            var slices = package.Entries
                .Where(e => e.FullName.Contains($"{framework}.xcframework/", StringComparison.Ordinal))
                .Select(e => e.FullName)
                .ToList();

            Assert.True(slices.Count > 0, $"{Packages.IOS} does not ship {framework}.xcframework.");

            Assert.Contains(slices, name => name.Contains("/ios-arm64/", StringComparison.Ordinal));
            Assert.Contains(slices, name => name.Contains("-simulator/", StringComparison.Ordinal));
        }
    }

    // buildTransitive rather than build: these packages are normally reached transitively, and
    // NuGet imports build/ only for direct references - so targets packed under build/ would
    // silently never run for the consumers who matter most.
    [SkippableFact]
    public void Ios_package_ships_targets_that_require_the_consumers_sdk()
    {
        Skip.IfNot(Packages.Exists(Packages.IOS), "the iOS package is only built on macOS");

        using var package = Packages.OpenPackage(Packages.IOS);

        var targets = package.GetEntry($"buildTransitive/{Packages.IOS}.targets");
        Assert.True(targets is not null, $"{Packages.IOS} is missing buildTransitive/{Packages.IOS}.targets.");

        using var reader = new StreamReader(targets!.Open());
        var content = reader.ReadToEnd();

        // Without this the app links against a facade whose Red5 symbols nothing supplies, and
        // fails with an undefined-symbol error that names none of the cause.
        Assert.Contains("Red5ProIosSdk", content);
        Assert.Contains("RED5101", content);
    }

    [SkippableFact]
    public void Android_package_ships_targets_that_require_the_consumers_sdk()
    {
        Skip.IfNot(Packages.Exists(Packages.Android), "the Android package was not built here");

        using var package = Packages.OpenPackage(Packages.Android);

        var targets = package.GetEntry($"buildTransitive/{Packages.Android}.targets");
        Assert.True(targets is not null, $"{Packages.Android} is missing buildTransitive/{Packages.Android}.targets.");

        using var reader = new StreamReader(targets!.Open());
        var content = reader.ReadToEnd();

        Assert.Contains("Red5ProAndroidSdk", content);
        Assert.Contains("RED5001", content);

        // The check must not fire for class libraries - only the app dexes the Java. Getting this
        // wrong makes every intermediate package in a chain demand a licensed .aar it never uses.
        Assert.Contains("AndroidApplication", content);
    }

    public static IEnumerable<object[]> CrossPlatformPackagesAndFrameworks =>
        from packageId in new[] { Packages.Core, Packages.Maui }
        from tfm in Packages.AndroidTargetFrameworks.Concat(Packages.IosTargetFrameworks)
        select new object[] { packageId, tfm };

    [SkippableTheory]
    [MemberData(nameof(CrossPlatformPackagesAndFrameworks))]
    public void Cross_platform_packages_carry_an_assembly_for_every_target_framework(
        string packageId, string tfm)
    {
        Skip.IfNot(Packages.Exists(packageId), $"{packageId} is only built on macOS");

        using var package = Packages.OpenPackage(packageId);

        // These span both platforms, so unlike the bindings they must be present for all six target
        // frameworks — a consumer multi-targeting Android and iOS resolves the same package on both
        // legs. This is where a failed merge pass shows up.
        var expected = $"lib/{tfm}/{packageId}.dll";
        Assert.True(
            package.GetEntry(expected) is not null,
            $"{packageId} is missing '{expected}'.");
    }
}
