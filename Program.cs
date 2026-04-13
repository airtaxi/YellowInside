using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading;

namespace YellowInside;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var isRedirect = DecideRedirection();
        if (isRedirect) return;

        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    private static bool DecideRedirection()
    {
        var currentInstance = AppInstance.FindOrRegisterForKey("YellowInside_SingleInstance");

        if (!currentInstance.IsCurrent)
        {
            var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            currentInstance.RedirectActivationToAsync(activationArguments).AsTask().Wait();
            return true;
        }

        currentInstance.Activated += OnAppInstanceActivated;
        return false;
    }

    private static void OnAppInstanceActivated(object sender, AppActivationArguments arguments)
    {
        App.ShowManageWindow();
    }
}
