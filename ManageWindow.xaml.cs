using YellowInside.Messages;
using YellowInside.Pages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace YellowInside;

public sealed partial class ManageWindow : WindowEx, IRecipient<LaunchOnStartupChangedMessage>
{
    public static ManageWindow Instance { get; private set; }

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;

    private delegate nint Subclassprocedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint windowHandle, Subclassprocedure procedure, nuint subclassId, nuint referenceData);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);

    private readonly Subclassprocedure _subclassprocedure;
    private bool _forceClose;
    private bool _systemShutdown;

    public ManageWindow()
    {
        Instance = this;
        InitializeComponent();

        AppWindow.SetIcon("Assets/Icon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        UpdateLaunchOnStartupMenuFlyoutItemText();

        WeakReferenceMessenger.Default.Register(this);

        _subclassprocedure = WindowSubclassProc;
        SetWindowSubclass(this.GetWindowHandle(), _subclassprocedure, 1, 0);

        AppFrame.Navigated += OnAppFrameNavigated;
        AppFrame.Navigate(typeof(ManagePage));
    }

    private void OnAppFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e) => AppTitleBar.IsPaneToggleButtonVisible = e.SourcePageType == typeof(ManagePage);

    private nint WindowSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        switch (message)
        {
            case WM_QUERYENDSESSION:
                _systemShutdown = true;
                return 1;

            case WM_ENDSESSION:
                if (wParam != 0) App.Shutdown();
                return 0;

            case WM_CLOSE:
                if (_forceClose || _systemShutdown)
                    break;
                this.Hide();
                return 0;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    public void Receive(LaunchOnStartupChangedMessage message)
    {
        DispatcherQueue.TryEnqueue(UpdateLaunchOnStartupMenuFlyoutItemText);
    }

    public static void ShowLoading(string message)
    {
        Instance.DispatcherQueue.TryEnqueue(() =>
        {
            Instance.AppFrame.IsEnabled = false;
            Instance.LoadingGrid.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(message))
            {
                Instance.LoadingTextBlock.Text = message;
                Instance.LoadingTextBlock.Visibility = Visibility.Visible;
            }
            else Instance.LoadingTextBlock.Visibility = Visibility.Collapsed;
        });
    }

    public static void HideLoading()
    {
        Instance.DispatcherQueue.TryEnqueue(() =>
        {
            Instance.LoadingGrid.Visibility = Visibility.Collapsed;
            Instance.LoadingTextBlock.Visibility = Visibility.Collapsed;
            Instance.AppFrame.IsEnabled = true;
        });
    }

    private void UpdateLaunchOnStartupMenuFlyoutItemText()
    {
        if (App.LaunchOnStartup) LaunchOnStartupMenuFlyoutItem.Text = "시스템 시작 시 자동 실행 해제";
        else LaunchOnStartupMenuFlyoutItem.Text = "시스템 시작 시 자동 실행 설정";
    }

    private void OnOpenManageWindowMenuFlyoutItemClicked(object sender, RoutedEventArgs e) => App.ShowManageWindow();
    private void OnCloseProgramMenuFlyoutItemClicked(object sender, RoutedEventArgs e) => Environment.Exit(0);
    
    private void OnLaunchOnStartupMenuFlyoutItemClicked(object sender, RoutedEventArgs e)
    {
        App.LaunchOnStartup = !App.LaunchOnStartup;
        UpdateLaunchOnStartupMenuFlyoutItemText();
        WeakReferenceMessenger.Default.Send(new LaunchOnStartupChangedMessage(App.LaunchOnStartup));
    }

    private void OnAppTitleBarBackRequested(TitleBar sender, object args)
    {
        if (AppFrame.CanGoBack)
        {
            AppFrame.GoBack();
        }
    }

    private void OnAppTitleBarPaneToggleRequested(TitleBar sender, object args)
    {
        if (AppFrame.Content is ManagePage managePage) managePage.ToggleNavigationPane();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public static void Navigate(Type pageType, object args = null) => Instance.AppFrame.Navigate(pageType, args);

    public static void GoBack()
    {
        if (Instance.AppFrame.CanGoBack) Instance.AppFrame.GoBack();
    }
}
