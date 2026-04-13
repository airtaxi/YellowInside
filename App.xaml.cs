using dccon.NET;
using YellowInsideLib;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace YellowInside;

public partial class App : Application
{
    private static Window s_manageWindow;
    private static PopupWindow s_dcconPopupWindow;
    public static DcconClient DcconClient { get; } = new DcconClient();

    public static bool LaunchOnStartup
    {
        get
        {
            var startupTask = Task.Run(async () => await StartupTask.GetAsync("YellowInsideStartup")).GetAwaiter().GetResult();
            return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        set
        {
            var startupTask = Task.Run(async () => await StartupTask.GetAsync("YellowInsideStartup")).GetAwaiter().GetResult();
            if (value) Task.Run(async () => await startupTask.RequestEnableAsync()).GetAwaiter().GetResult();
            else startupTask.Disable();
        }
    }

    public App()
    {
        InitializeComponent();

        var manager = SessionManager.Instance;
        manager.Log += (text) => Debug.WriteLine(text);
        manager.DcconButtonClicked += OnDcconButtonClicked;

        var buttonIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "dccon.png");
        manager.Start(buttonIconPath);
    }

    public static void ShowManageWindow() => s_manageWindow.Activate();

    private static void OnDcconButtonClicked(SessionInfo info)
    {
        ManageWindow.Instance?.DispatcherQueue.TryEnqueue(() =>
        {
            try { s_dcconPopupWindow?.Close(); }
            catch { }
            s_dcconPopupWindow = null;

            s_dcconPopupWindow = new PopupWindow(info);
            s_dcconPopupWindow.Closed += (_, _) => s_dcconPopupWindow = null;
            s_dcconPopupWindow.Activate();
        });
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        await ContentsManager.InitializeAsync();

        s_manageWindow = new ManageWindow();
    }
}
