using YellowInside.Messages;
using YellowInside.Pages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace YellowInside;

public sealed partial class ManageWindow : WindowEx, IRecipient<LaunchOnStartupChangedMessage>
{
    public static ManageWindow Instance { get; private set; }

    public ManageWindow()
    {
        Instance = this;
        InitializeComponent();

        AppWindow.SetIcon("Assets/Icon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        UpdateLaunchOnStartupMenuFlyoutItemText();

        WeakReferenceMessenger.Default.Register(this);

        AppFrame.Navigate(typeof(ManagePage));
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        this.Hide();
    }

    public static void Navigate(Type pageType, object args = null) => Instance.AppFrame.Navigate(pageType, args);
}
