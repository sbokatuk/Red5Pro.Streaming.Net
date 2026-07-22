namespace Red5Pro.Streaming.Net.Sample;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    // A plain NavigationPage rather than the template's Shell: the sample is one page, and Shell
    // adds a flyout and routing that would be noise around the streaming code.
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(new MainPage()));
}
