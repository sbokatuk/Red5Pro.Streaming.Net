using Android.App;
using Android.OS;
using Android.Util;
using Android.Widget;

namespace Red5Pro.Streaming.Net.Android.DeviceTests;

/// <summary>
/// Runs the smoke tests on launch and reports the verdict to logcat under the Red5E2E tag.
/// .github/scripts/run-android-device-tests.sh polls for RED5_E2E_DONE and turns it into an exit
/// code, so the exact marker strings are part of the contract with that script.
/// </summary>
// Name is pinned because .NET for Android otherwise generates a hashed Java class name
// (crc64....MainActivity), and the runner script launches this activity by name with
// `am start -n <package>/.MainActivity`.
[Activity(
    Name = "net.red5.streaming.devicetests.MainActivity",
    Label = "Red5 device tests",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    private const string Tag = "Red5E2E";
    private const string DoneMarker = "RED5_E2E_DONE";

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var output = new TextView(this);
        SetContentView(output);

        var failures = 0;
        var total = 0;

        foreach (var (name, run) in SmokeTests.All)
        {
            total++;

            try
            {
                run(this, detail => Log.Info(Tag, $"    {detail}"));
                Log.Info(Tag, $"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                // The whole exception, not just the message: an UnsatisfiedLinkError's stack is
                // what tells you which native library failed to load.
                Log.Error(Tag, $"FAIL {name}: {exception}");
            }
        }

        // Passed as intent extras by the runner script. Absent on most runs, and reported as
        // skipped either way so a run without credentials cannot be mistaken for one that proved
        // streaming works.
        var host = Intent?.GetStringExtra("host");
        var licenseKey = Intent?.GetStringExtra("licenseKey");
        var cloud = Intent?.GetStringExtra("deployment") != "standalone";

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(licenseKey))
        {
            Log.Info(Tag, "SKIP licence validation (no host/licenseKey extras)");
            Log.Info(Tag, "SKIP live publish (no host/licenseKey extras)");
        }
        else
        {
            var options = LiveStreamTest.OptionsFor(host, licenseKey, cloud);
            var deployment = cloud ? "cloud" : "standalone";

            // One check covering both, because they share a client - see ValidateAndPublishAsync.
            total++;

            try
            {
                var (license, published) = await LiveStreamTest.ValidateAndPublishAsync(this, options);
                Log.Info(Tag, $"    {license}");
                Log.Info(Tag, $"    {published}");
                Log.Info(Tag, $"PASS live publish ({deployment})");
            }
            catch (Exception exception)
            {
                failures++;
                Log.Error(Tag, $"FAIL live publish ({deployment}): {exception}");
            }
        }

        var verdict = failures == 0
            ? $"{DoneMarker} PASS ({total} checks)"
            : $"{DoneMarker} FAIL ({failures} of {total} checks failed)";

        Log.Info(Tag, verdict);
        output.Text = verdict;
    }
}
