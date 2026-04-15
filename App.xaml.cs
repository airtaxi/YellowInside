using dccon.NET;
using YellowInside.Managers;
using YellowInsideLib;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace YellowInside;

public partial class App : Application
{
    private static ManageWindow s_manageWindow;
    private static PopupWindow s_dcconPopupWindow;
    public static DcconClient DcconClient { get; } = new DcconClient();
    public static HotkeyManager HotkeyManager { get; } = new();

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

    public static void ShowManageWindow()
    {
        s_manageWindow.DispatcherQueue.TryEnqueue(() =>
        {
            s_manageWindow.Activate();
            s_manageWindow.BringToFront();
        });
    }

    public static void Shutdown()
    {
        UpdateCheckManager.Stop();
        HotkeyManager.Dispose();
        SessionManager.Instance.Dispose();

        s_manageWindow?.ForceClose();
    }

    private static void OnDcconButtonClicked(SessionInfo info) => OpenDcconPopup(info);

    private static void OnHotkeyPressed(nint foregroundWindowHandle) =>
        OpenDcconPopup(new SessionInfo(foregroundWindowHandle, "", false));

    private static void OpenDcconPopup(SessionInfo sessionInfo)
    {
        ManageWindow.Instance?.DispatcherQueue.TryEnqueue(() =>
        {
            try { s_dcconPopupWindow?.Close(); }
            catch { }
            s_dcconPopupWindow = null;

            s_dcconPopupWindow = new PopupWindow(sessionInfo);
            s_dcconPopupWindow.Closed += (_, _) => s_dcconPopupWindow = null;
            s_dcconPopupWindow.Activate();
        });
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await ContentsManager.InitializeAsync();
        HistoryManager.Initialize();

        s_manageWindow = new ManageWindow();

        if (s_manageWindow.Content is FrameworkElement rootElement)
            rootElement.RequestedTheme = SettingsManager.GetElementTheme();

        var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activationArguments.Kind != ExtendedActivationKind.StartupTask)
            s_manageWindow.Activate();

        UpdateCheckManager.Start();

        HotkeyManager.HotkeyPressed += OnHotkeyPressed;
        if (SettingsManager.HotkeyEnabled)
            HotkeyManager.Start(SettingsManager.HotkeyModifiers, SettingsManager.HotkeyKey);
    }
}
