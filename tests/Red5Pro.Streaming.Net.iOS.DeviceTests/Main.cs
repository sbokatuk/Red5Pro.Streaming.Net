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

        if (string.IsNullOrEmpty(licenseKey) || string.IsNullOrEmpty(host))
        {
            // Reported either way, so a run with no credentials cannot be mistaken for one that
            // proved the licence works.
            Console.WriteLine("SKIP licence validation (no RED5_LICENSE_KEY / RED5_ENDPOINT)");
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

                // Red5's rejection message is always the bare string "License validation failed",
                // with no reason, so the plausible causes are listed here instead. Observed:
                // both a Red5 Pro SDK key and a Red5 Cloud key are rejected identically against a
                // valid stream manager endpoint, which points at the application rather than the
                // key - Red5 licences are tied to an application identity, and this test app's
                // bundle id (net.red5.streaming.devicetests) is not one of yours.
                Console.WriteLine(
                    "    Red5 reports no reason for a rejection. Things to check, in order:\n" +
                    "      - the bundle id this app was built with is registered against the key\n" +
                    "      - the key is the *SDK* licence, not the server licence\n" +
                    "      - the key matches the deployment (a Cloud key for a Cloud endpoint)\n" +
                    "      - the trial has not expired");
            }
        }

        Console.WriteLine(failures == 0
            ? $"{DoneMarker} PASS ({total} checks)"
            : $"{DoneMarker} FAIL ({failures} of {total} checks failed)");

        Console.Out.Flush();
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
