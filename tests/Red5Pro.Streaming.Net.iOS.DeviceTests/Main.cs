using Foundation;
using UIKit;

namespace Red5Pro.Streaming.Net.IOS.DeviceTests;

public static class Application
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

/// <summary>
/// Runs the smoke tests once the app has launched, prints the verdict to stdout and exits.
/// .github/scripts/run-ios-device-tests.sh launches the app with `simctl launch --console-pty`,
/// which streams stdout and returns when the process ends — so exiting here is what ends the CI
/// step, and the marker strings are part of the contract with that script.
/// </summary>
[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    private const string DoneMarker = "RED5_E2E_DONE";

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // Deferred to the next turn of the run loop rather than run inline: the frameworks are
        // loaded as part of launch, and exiting from inside FinishedLaunching can cut off stdout
        // before the pipe is drained.
        NSRunLoop.Main.BeginInvokeOnMainThread(() => _ = RunAsync());
        return true;
    }

    private static async Task RunAsync()
    {
        var failures = 0;
        var total = 0;

        foreach (var (name, run) in SmokeTests.Offline)
        {
            total++;

            try
            {
                run(detail => Console.WriteLine($"    {detail}"));
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                // The whole exception: for a missing framework the stack is what identifies which
                // one failed to load.
                Console.WriteLine($"FAIL {name}: {exception}");
            }
        }

        // Environment rather than launch arguments: simctl passes the environment through
        // SIMCTL_CHILD_*, and a licence key does not belong on a command line that ps can read.
        var licenseKey = Environment.GetEnvironmentVariable("RED5_LICENSE_KEY");
        var host = Environment.GetEnvironmentVariable("RED5_ENDPOINT");
        var isCloud = Environment.GetEnvironmentVariable("RED5_DEPLOYMENT") != "standalone";

        // The endpoint is what gates this tier, not the key: Red5's documentation configures a
        // Cloud client without a key at all, so "no key" is a case worth being able to test rather
        // than a reason to skip.
        if (string.IsNullOrEmpty(host))
        {
            // Reported either way, so a run with no credentials cannot be mistaken for one that
            // proved the licence works.
            Console.WriteLine("SKIP licence validation (no RED5_ENDPOINT)");
        }
        else
        {
            total++;

            try
            {
                var validation = SmokeTests.ValidateLicenseAsync(licenseKey, host, isCloud);
                var completed = await Task.WhenAny(validation, Task.Delay(TimeSpan.FromSeconds(45)));

                if (completed != validation)
                {
                    throw new TimeoutException(
                        $"no licence verdict from {host} within 45s. The SDK reports nothing at " +
                        "all when it cannot reach the server, so this is as likely to be " +
                        "connectivity as a bad key.");
                }

                Console.WriteLine($"    {await validation}");
                Console.WriteLine($"PASS licence validation ({(isCloud ? "cloud" : "standalone")})");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL licence validation: {exception.Message}");

                // Red5 distinguishes only two rejection messages, and the difference is the useful
                // part:
                //
                //   "No license key provided"    the key never reached the SDK
                //   "License validation failed"  a key reached it and a service said no, with no
                //                                reason given
                //
                // Note that Red5's own iOS documentation omits setLicenseKey from both its Quick
                // Start and its Full Working Example. That is wrong - configuring a Cloud client
                // exactly as documented produces "No license key provided" - so do not conclude
                // from those samples that the key is optional.
                //
                // Observed here: a Red5 Pro SDK key and a Red5 Cloud SDK key, issued by two
                // separate accounts, are rejected identically against a valid Cloud stream manager
                // endpoint. Two independent credentials failing the same way points at the
                // application rather than at either key.
                Console.WriteLine(
                    "    Red5 gives no reason for a rejection. Things to check, in order:\n" +
                    "      - whether the key is bound to an application identity, and if so whether\n" +
                    "        this app's bundle id is the registered one (the legacy SDK made this\n" +
                    "        explicit with setBundleID; the 2.x SDK appears to read it implicitly)\n" +
                    "      - the key is the *SDK* licence, not the server licence - accounts issue both\n" +
                    "      - the key matches the deployment (a Cloud key for a Cloud endpoint)\n" +
                    "      - the trial has not expired, and has been activated if it needs it");
            }
        }

        Console.WriteLine(failures == 0
            ? $"{DoneMarker} PASS ({total} checks)"
            : $"{DoneMarker} FAIL ({failures} of {total} checks failed)");

        Console.Out.Flush();
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
