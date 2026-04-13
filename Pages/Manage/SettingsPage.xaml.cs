using YellowInside.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace YellowInside.Pages.Manage;

public sealed partial class SettingsPage : Page
{
    private bool _isInitializing = true;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _isInitializing = true;

        ThemeComboBox.SelectedIndex = (int)SettingsManager.Theme;

        GifPlaybackToggleSwitch.IsOn = SettingsManager.GifPlaybackEnabled;
        GifWarningInfoBar.IsOpen = SettingsManager.GifPlaybackEnabled;

        _isInitializing = false;
    }

    private async void OnThemeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        var selectedTheme = (AppThemeSetting)ThemeComboBox.SelectedIndex;
        SettingsManager.Theme = selectedTheme;

        ApplyThemeToAllWindows();

        await this.ShowDialogAsync(
            "테마 변경",
            "일부 UI 요소는 프로그램을 재시작해야 올바르게 반영됩니다.");
    }

    private static void ApplyThemeToAllWindows()
    {
        var elementTheme = SettingsManager.GetElementTheme();

        if (ManageWindow.Instance?.Content is FrameworkElement manageWindowContent)
            manageWindowContent.RequestedTheme = elementTheme;

        // PopupWindow is transient; it will pick up the theme when next created.
    }

    private void OnGifPlaybackToggleSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        SettingsManager.GifPlaybackEnabled = GifPlaybackToggleSwitch.IsOn;
        GifWarningInfoBar.IsOpen = GifPlaybackToggleSwitch.IsOn;
    }

    private async void OnExportButtonClicked(object sender, RoutedEventArgs e)
    {
        var savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("YellowInside 패키지", [".yip"]);
        savePicker.SuggestedFileName = "YellowInside_Export";

        InitializeWithWindow.Initialize(savePicker, ManageWindow.Instance.GetWindowHandle());

        var file = await savePicker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            ManageWindow.ShowLoading("패키지를 내보내는 중...");
            await ContentsManager.ExportAsync(file.Path);
            ManageWindow.HideLoading();

            await this.ShowDialogAsync("내보내기 완료", "패키지를 성공적으로 내보냈습니다.");
        }
        catch (Exception exception)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("내보내기 실패", exception.Message);
        }
    }

    private async void OnImportButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        openPicker.FileTypeFilter.Add(".yip");
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        InitializeWithWindow.Initialize(openPicker, ManageWindow.Instance.GetWindowHandle());

        var file = await openPicker.PickSingleFileAsync();
        if (file is null) return;

        var modeResult = await this.ShowDialogAsync(
            "불러오기 모드 선택",
            "기존 데이터를 모두 삭제하고 불러올까요?\n'확인'을 누르면 기존 데이터를 대체합니다.\n'추가만'을 누르면 기존 데이터에 추가합니다.",
            primaryButtonText: "대체",
            secondaryButtonText: "추가만");

        if (modeResult == ContentDialogResult.None) return;

        var replaceAll = modeResult == ContentDialogResult.Primary;

        try
        {
            ManageWindow.ShowLoading("패키지를 불러오는 중...");
            await ContentsManager.ImportAsync(file.Path, replaceAll);
            ManageWindow.HideLoading();

            await this.ShowDialogAsync("불러오기 완료", "패키지를 성공적으로 불러왔습니다.");
        }
        catch (Exception exception)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("불러오기 실패", exception.Message);
        }
    }
}
