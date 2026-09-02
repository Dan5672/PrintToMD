using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Print2Md.App;

sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();
        Suspending += OnSuspending;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var rootFrame = Window.Current.Content as Frame;
        if (rootFrame == null)
        {
            rootFrame = new Frame();
            Window.Current.Content = rootFrame;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        Window.Current.Activate();
    }

    private static void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs args)
    {
        var deferral = args.SuspendingOperation.GetDeferral();
        deferral.Complete();
    }
}
