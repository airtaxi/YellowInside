using Arcacon.NET;
using dccon.NET;
using InvenSticker.NET;
using YellowInside.Managers;
using YellowInsideLib;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace YellowInside;

public partial class App : Application
{
    private static ManageWindow s_manageWindow;
    private static PopupWindow s_dcconPopupWindow;
    public static ArcaconClient ArcaconClient { get; } = new ArcaconClient();
    public static DcconClient DcconClient { get; } = new DcconClient();
    public static InvenStickerClient InvenStickerClient { get; } = new InvenStickerClient();
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

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var manager = SessionManager.Instance;
        manager.InfoLog += (text) => { Debug.WriteLine(text); Managers.FileLogManager.WriteInfo(text); };
        manager.WarnLog += (text) => { Debug.WriteLine(text); Managers.FileLogManager.WriteWarn(text); };
        manager.ErrorLog += (text) => { Debug.WriteLine(text); Managers.FileLogManager.WriteError(text); };
        manager.DcconButtonClicked += OnDcconButtonClicked;

        var buttonIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "dccon.png");
        manager.Start(buttonIconPath);
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException("UnhandledException", e.Exception);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            LogException("AppDomain.UnhandledException", exception);
    }

    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string source, Exception exception)
    {
        var builder = new StringBuilder();
        builder.Append($"[{source}] ");

        var current = exception;
        var depth = 0;
        while (current is not null)
        {
            if (depth > 0) builder.Append(" → [InnerException] ");
            builder.Append($"{current.GetType().Name}: {current.Message}\n{current.StackTrace}");
            current = current.InnerException;
            depth++;
        }

        var message = builder.ToString();
        Debug.WriteLine(message);
        Managers.FileLogManager.WriteError(message);
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
        ArcaconClient.DisposeAsync().GetAwaiter().GetResult();
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
