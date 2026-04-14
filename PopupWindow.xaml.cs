using YellowInside.ViewModels;
using YellowInsideLib;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using WinUIEx;
using Windows.Storage;

namespace YellowInside;

public sealed partial class PopupWindow : WindowEx
{
    private const int PopupWidth = 400;
    private const int PopupHeight = 560;

    public PopupViewModel ViewModel { get; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const string SettingsKeyRightClickTipDismissed = "PopupRightClickTipDismissed";

    public PopupWindow(SessionInfo sessionInfo)
    {
        InitializeComponent();

        ViewModel = new PopupViewModel(sessionInfo.ChatHwnd, OnStickerClicked);

        AppWindow.SetIcon("Assets/Icon.ico");

        ConfigureWindow();
        PositionNearChatWindow(sessionInfo.ChatHwnd);

        Activated += OnActivated;
        CategoryGridView.Loaded += OnCategoryGridViewLoaded;
        StickerGridView.Loaded += OnStickerGridViewLoaded;
    }

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);

        var dpi = GetDpiForWindow(this.GetWindowHandle());
        var scale = dpi / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(PopupWidth * scale),
            (int)(PopupHeight * scale)));
    }

    private void PositionNearChatWindow(nint chatHwnd)
    {
        if (chatHwnd == 0) return;
        if (!GetWindowRect(chatHwnd, out var chatRect)) return;

        var windowSize = AppWindow.Size;
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;

        // 채팅창 오른쪽에, 하단 정렬
        var x = chatRect.Right;
        var y = chatRect.Bottom - windowSize.Height;

        // 화면 밖으로 나가면 왼쪽에 배치
        if (x + windowSize.Width > workArea.X + workArea.Width)
            x = chatRect.Left - windowSize.Width;

        if (x < workArea.X) x = workArea.X;
        if (y < workArea.Y) y = workArea.Y;
        if (y + windowSize.Height > workArea.Y + workArea.Height)
            y = workArea.Y + workArea.Height - windowSize.Height;

        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) Close();
        else BringToFront();
    }

    private void OnCategoryGridViewLoaded(object sender, RoutedEventArgs e)
    {
        CategoryGridView.SelectedIndex = ViewModel.GetInitialCategoryIndex();
    }

    private void OnStickerGridViewLoaded(object sender, RoutedEventArgs e)
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.ContainsKey(SettingsKeyRightClickTipDismissed)) return;
        RightClickTeachingTip.IsOpen = true;
    }

    private void OnRightClickTeachingTipActionButtonClicked(TeachingTip sender, object args)
    {
        ApplicationData.Current.LocalSettings.Values[SettingsKeyRightClickTipDismissed] = true;
        RightClickTeachingTip.IsOpen = false;
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => Close();

    private void OnCategoryGridViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryGridView.SelectedIndex >= 0)
            ViewModel.SelectCategory(CategoryGridView.SelectedIndex);
    }

    private void OnStickerGridViewRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as FrameworkElement;
        while (element is not null)
        {
            if (element.DataContext is PopupStickerViewModel sticker)
            {
                ViewModel.TogglePending(sticker);
                return;
            }
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
    }

    private async void OnStickerClicked(PopupStickerViewModel sticker)
    {
        if (ViewModel.PendingStickers.Count > 0)
        {
            ViewModel.TogglePending(sticker);
            var filePaths = ViewModel.GetPendingFilePaths();
            if (filePaths.Count == 0) return;

            await SessionManager.Instance.SendMultipleDcconsAsync(ViewModel.ChatHwnd, filePaths);
            ViewModel.ClearPending();
            Close();
            return;
        }

        await SessionManager.Instance.SendDcconAsync(ViewModel.ChatHwnd, sticker.LocalFilePath);
        Close();
    }

    private async void OnSendPendingButtonClicked(object sender, RoutedEventArgs e)
    {
        var filePaths = ViewModel.GetPendingFilePaths();
        if (filePaths.Count == 0) return;

        await SessionManager.Instance.SendMultipleDcconsAsync(ViewModel.ChatHwnd, filePaths);
        ViewModel.ClearPending();
        Close();
    }

    private void OnClearPendingButtonClicked(object sender, RoutedEventArgs e) => ViewModel.ClearPending();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        CategoryGridView.Loaded -= OnCategoryGridViewLoaded;
        StickerGridView.Loaded -= OnStickerGridViewLoaded;

        ViewModel.Cleanup();
    }
}
