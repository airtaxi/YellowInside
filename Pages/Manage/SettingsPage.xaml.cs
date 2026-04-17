using Arcacon.NET.Exceptions;
using YellowInside.Dialogs;
using YellowInside.Helpers;
using YellowInside.Managers;
using YellowInside.Messages;
using YellowInside.Models;
using YellowInside.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using DevWinUI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using WinUIEx;
using Windows.Storage;

namespace YellowInside.Pages.Manage;

public sealed partial class SettingsPage : Page, IRecipient<LaunchOnStartupChangedMessage>
{
    private bool _isInitializing = true;

    public SettingsPage()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(LaunchOnStartupChangedMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _isInitializing = true;
            LaunchOnStartupToggleSwitch.IsOn = message.Value;
            _isInitializing = false;
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _isInitializing = true;

        ThemeComboBox.SelectedIndex = (int)SettingsManager.Theme;

        GifPlaybackToggleSwitch.IsOn = SettingsManager.GifPlaybackEnabled;
        GifWarningInfoBar.IsOpen = SettingsManager.GifPlaybackEnabled;

        LaunchOnStartupToggleSwitch.IsOn = App.LaunchOnStartup;

        HotkeyToggleSwitch.IsOn = SettingsManager.HotkeyEnabled;
        UpdateHotkeyVisibility();
        PopulateHotkeyKeyVisuals(HotkeyDisplayPanel, SettingsManager.HotkeyModifiers, SettingsManager.HotkeyKey);

        var version = Windows.ApplicationModel.Package.Current.Id.Version;
        VersionTextBlock.Text = $"v{version.Major}.{version.Minor}.{version.Build}";

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

    private void OnLaunchOnStartupToggleSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        App.LaunchOnStartup = LaunchOnStartupToggleSwitch.IsOn;
        WeakReferenceMessenger.Default.Send(new LaunchOnStartupChangedMessage(LaunchOnStartupToggleSwitch.IsOn));
    }

    private async void OnCheckUpdateButtonClicked(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusTextBlock.Text = "업데이트를 확인하는 중...";

        try
        {
            var storeContext = StoreContext.GetDefault();
            var updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();

            if (updates.Count > 0)
            {
                UpdateStatusTextBlock.Text = "새로운 업데이트가 있습니다.";
                var result = await this.ShowDialogAsync(
                    "업데이트 확인",
                    "새로운 버전이 출시되었습니다. Microsoft Store에서 업데이트하시겠습니까?",
                    primaryButtonText: "업데이트",
                    secondaryButtonText: "나중에");

                if (result == ContentDialogResult.Primary)
                {
                    var packageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
                    await Launcher.LaunchUriAsync(new Uri($"ms-windows-store://pdp/?PFN={packageFamilyName}"));
                }
            }
            else UpdateStatusTextBlock.Text = "현재 최신 버전입니다.";
        }
        catch { UpdateStatusTextBlock.Text = "업데이트 확인에 실패했습니다."; }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    private async void OnExportButtonClicked(object sender, RoutedEventArgs e)
    {
        var exportFavorites = await AskExportFavoritesAsync();
        if (exportFavorites is null) return;

        var file = await PickPackageExportFileAsync("YellowInside_Export");
        if (file is null) return;

        await ExportPackagesAsync(file.Path, "패키지를 내보내는 중...", exportFavorites: exportFavorites.Value);
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

        var importFavorites = false;
        try
        {
            var hasFavorites = await ContentsManager.HasFavoritesInImportFileAsync(file.Path);
            if (hasFavorites)
            {
                var favoriteResult = await this.ShowDialogAsync(
                    "즐겨찾기 불러오기",
                    "불러올 데이터에 즐겨찾기가 포함되어 있습니다.\n즐겨찾기도 함께 불러오시겠습니까?",
                    primaryButtonText: "예",
                    secondaryButtonText: "아니오");

                if (favoriteResult == ContentDialogResult.None) return;

                importFavorites = favoriteResult == ContentDialogResult.Primary;
            }
        }
        catch (Exception exception)
        {
            await this.ShowDialogAsync("불러오기 실패", exception.Message);
            return;
        }

        try
        {
            if (replaceAll && !await ConfirmReplaceImportAsync(file.Path)) return;

            ManageWindow.ShowLoading("패키지를 불러오는 중...");
            await Task.Run(async () => await ContentsManager.ImportAsync(file.Path, replaceAll, importFavorites));
            ManageWindow.HideLoading();

            await this.ShowDialogAsync("불러오기 완료", "패키지를 성공적으로 불러왔습니다.");
        }
        catch (Exception exception)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("불러오기 실패", exception.Message);
        }
    }

    private async void OnPartialExportButtonClicked(object sender, RoutedEventArgs e)
    {
        var downloadedPackages = ContentsManager.GetDownloadedPackages();
        if (downloadedPackages.Count == 0)
        {
            await this.ShowDialogAsync("부분 내보내기", "내보낼 패키지가 없습니다.");
            return;
        }

        var exportItems = downloadedPackages.Select(package => (PackageSelectionListViewModel)new PartialPackageExportListViewModel(package));
        var packageSelectionDialog = new PackageSelectionDialog(
            "패키지 일부 내보내기",
            "내보낼 패키지를 선택하세요.",
            "내보내기",
            exportItems)
        {
            XamlRoot = XamlRoot,
        };

        var dialogResult = await packageSelectionDialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary) return;

        var selectedPackageKeys = packageSelectionDialog.SelectedPackageKeys;
        if (selectedPackageKeys.Count == 0) return;

        var exportFavorites = await AskExportFavoritesAsync(selectedPackageKeys);
        if (exportFavorites is null) return;

        var file = await PickPackageExportFileAsync("YellowInside_PartialExport");
        if (file is null) return;

        await ExportPackagesAsync(file.Path, "선택한 패키지를 내보내는 중...", selectedPackageKeys, exportFavorites.Value);
    }

    private async void OnPartialImportButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        openPicker.FileTypeFilter.Add(".yip");
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        InitializeWithWindow.Initialize(openPicker, ManageWindow.Instance.GetWindowHandle());

        var file = await openPicker.PickSingleFileAsync();
        if (file is null) return;

        IReadOnlyList<StickerPackage> packages;
        try { packages = await ContentsManager.ReadPackagesFromImportFileAsync(file.Path); }
        catch (Exception exception)
        {
            await this.ShowDialogAsync("불러오기 실패", exception.Message);
            return;
        }

        if (packages.Count == 0)
        {
            await this.ShowDialogAsync("일부 불러오기", "불러올 패키지가 없습니다.");
            return;
        }

        // Extract thumbnails to temp directory, load into memory, then clean up immediately
        var importItems = new List<PackageSelectionListViewModel>();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"YellowInside_PartialImport_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            await Task.Run(() => ContentsManager.ExtractMainImagesFromImportFile(file.Path, packages, temporaryDirectory));

            foreach (var package in packages)
                importItems.Add(await PartialPackageImportListViewModel.CreateAsync(package, temporaryDirectory));
        }
        finally
        {
            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true); }
            catch { /* Temp cleanup failure is non-critical */ }
        }

        var packageSelectionDialog = new PackageSelectionDialog(
            "패키지 일부 불러오기",
            "불러올 패키지를 선택하세요.",
            "불러오기",
            importItems)
        {
            XamlRoot = XamlRoot,
        };

        var dialogResult = await packageSelectionDialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary) return;

        var selectedPackageKeys = packageSelectionDialog.SelectedPackageKeys;
        if (selectedPackageKeys.Count == 0) return;

        var importFavorites = false;
        try
        {
            var hasFavorites = await ContentsManager.HasFavoritesForPackagesInImportFileAsync(file.Path, selectedPackageKeys);
            if (hasFavorites)
            {
                var favoriteResult = await this.ShowDialogAsync(
                    "즐겨찾기 불러오기",
                    "선택한 패키지에 즐겨찾기가 포함되어 있습니다.\n즐겨찾기도 함께 불러오시겠습니까?",
                    primaryButtonText: "예",
                    secondaryButtonText: "아니오");

                if (favoriteResult == ContentDialogResult.None) return;

                importFavorites = favoriteResult == ContentDialogResult.Primary;
            }
        }
        catch (Exception exception)
        {
            await this.ShowDialogAsync("불러오기 실패", exception.Message);
            return;
        }

        try
        {
            ManageWindow.ShowLoading("선택한 패키지를 불러오는 중...");
            await Task.Run(async () => await ContentsManager.ImportAsync(file.Path, replaceAll: false, importFavorites, selectedPackageKeys));
            ManageWindow.HideLoading();

            await this.ShowDialogAsync("불러오기 완료", "선택한 패키지를 성공적으로 불러왔습니다.");
        }
        catch (Exception exception)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("불러오기 실패", exception.Message);
        }
    }

    private async void OnSynchronizeArcaconSubscriptionsButtonClicked(object sender, RoutedEventArgs e)
    {
        bool? includeInactiveArcacons = null;
        for (var synchronizationAttempt = 0; synchronizationAttempt < 3; synchronizationAttempt++)
        {
            try
            {
                var canUseArcaconSynchronization = await EnsureArcaconSynchronizationAvailableAsync();
                if (!canUseArcaconSynchronization) return;

                includeInactiveArcacons ??= await AskIncludeInactiveArcaconsAsync();
                if (includeInactiveArcacons is null) return;

                var synchronizedPackageCount = await SynchronizeArcaconSubscriptionsAsync(includeInactiveArcacons.Value);
                if (synchronizedPackageCount == 0)
                {
                    await this.ShowDialogAsync("아카콘 동기화", "동기화할 아카콘이 없습니다.");
                    return;
                }

                await this.ShowDialogAsync("아카콘 동기화 완료", $"{synchronizedPackageCount}개의 아카콘을 동기화했습니다.");
                return;
            }
            catch (Exception exception) when (exception is ArcaconLoginException or InvalidOperationException)
            {
                ManageWindow.HideLoading();
                var isLoginSuccessful = await ArcaconSessionHelper.ShowArcaconLoginDialogAsync(this);
                if (!isLoginSuccessful) return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ManageWindow.HideLoading();
                await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 아카콘을 동기화할 수 없습니다.");
                return;
            }
            catch (Exception exception)
            {
                ManageWindow.HideLoading();
                await this.ShowDialogAsync("아카콘 동기화 실패", exception.Message);
                return;
            }
        }
    }

    private void OnHotkeyToggleSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        SettingsManager.HotkeyEnabled = HotkeyToggleSwitch.IsOn;
        UpdateHotkeyVisibility();

        if (HotkeyToggleSwitch.IsOn)
            App.HotkeyManager.Start(SettingsManager.HotkeyModifiers, SettingsManager.HotkeyKey);
        else
            App.HotkeyManager.Stop();
    }

    private async void OnExportSendMethodButtonClicked(object sender, RoutedEventArgs e)
    {
        var savePicker = new FileSavePicker();
        savePicker.FileTypeChoices.Add("YellowInside 전송 설정", [".yic"]);
        savePicker.SuggestedFileName = "YellowInside_SendMethod";
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        InitializeWithWindow.Initialize(savePicker, ManageWindow.Instance.GetWindowHandle());

        var file = await savePicker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            await AppSendMethodManager.ExportAsync(file.Path);
            await this.ShowDialogAsync("내보내기 완료", "전송 설정을 성공적으로 내보냈습니다.");
        }
        catch (Exception exception) { await this.ShowDialogAsync("내보내기 실패", exception.Message); }
    }

    private async void OnImportSendMethodButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        openPicker.FileTypeFilter.Add(".yic");
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        InitializeWithWindow.Initialize(openPicker, ManageWindow.Instance.GetWindowHandle());

        var file = await openPicker.PickSingleFileAsync();
        if (file is null) return;

        var modeResult = await this.ShowDialogAsync(
            "불러오기 모드 선택",
            "기존 설정을 모두 삭제하고 불러올까요?\n'대체'를 누르면 기존 설정을 대체합니다.\n'병합'을 누르면 기존 설정에 병합합니다.",
            primaryButtonText: "대체",
            secondaryButtonText: "병합");

        if (modeResult == ContentDialogResult.None) return;

        var replaceAll = modeResult == ContentDialogResult.Primary;

        try
        {
            await AppSendMethodManager.ImportAsync(file.Path, replaceAll);
            await this.ShowDialogAsync("불러오기 완료", "전송 설정을 성공적으로 불러왔습니다.");
        }
        catch (Exception exception) { await this.ShowDialogAsync("불러오기 실패", exception.Message); }
    }

    private async void OnDownloadLogButtonClicked(object sender, RoutedEventArgs e)
    {
        if (!FileLogManager.HasLogs())
        {
            await this.ShowDialogAsync("로그 다운로드", "저장된 로그가 없습니다.");
            return;
        }

        var savePicker = new FileSavePicker();
        savePicker.FileTypeChoices.Add("텍스트 파일", [".txt"]);
        savePicker.SuggestedFileName = $"YellowInside_Log_{DateTime.Now:yyyyMMdd_HHmmss}";
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        InitializeWithWindow.Initialize(savePicker, ManageWindow.Instance.GetWindowHandle());

        var file = await savePicker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            File.Copy(FileLogManager.LogFilePath, file.Path, overwrite: true);
            await this.ShowDialogAsync("로그 다운로드 완료", "로그 파일을 성공적으로 저장했습니다.");
        }
        catch (Exception exception) { await this.ShowDialogAsync("로그 다운로드 실패", exception.Message); }
    }

    private void UpdateHotkeyVisibility()
    {
        var visibility = HotkeyToggleSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
        HotkeyDivider.Visibility = visibility;
        HotkeySettingGrid.Visibility = visibility;
    }

    private async void OnChangeHotkeyButtonClicked(object sender, RoutedEventArgs e)
    {
        uint capturedModifiers = 0;
        uint capturedKey = 0;

        var instructionText = new TextBlock
        {
            Text = "새 단축키를 입력하세요...\n(Ctrl, Shift, Alt 중 하나 이상 + 키)",
            TextWrapping = TextWrapping.Wrap,
        };
        var keyDisplayPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            MinHeight = 36,
        };
        var contentPanel = new StackPanel { Spacing = 16 };
        contentPanel.Children.Add(instructionText);
        contentPanel.Children.Add(keyDisplayPanel);

        var dialog = new ContentDialog
        {
            Title = "단축키 변경",
            Content = contentPanel,
            PrimaryButtonText = "확인",
            CloseButtonText = "취소",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            RequestedTheme = SettingsManager.GetElementTheme(),
        };

        dialog.PreviewKeyDown += (_, arguments) =>
        {
            arguments.Handled = true;

            var virtualKey = arguments.Key;

            if (virtualKey is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
                or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
                or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
                or VirtualKey.LeftWindows or VirtualKey.RightWindows)
                return;

            capturedModifiers = 0;
            if (IsVirtualKeyDown(VirtualKey.Control)) capturedModifiers |= HotkeyManager.ModifierControl;
            if (IsVirtualKeyDown(VirtualKey.Menu)) capturedModifiers |= HotkeyManager.ModifierAlt;
            if (IsVirtualKeyDown(VirtualKey.Shift)) capturedModifiers |= HotkeyManager.ModifierShift;

            if (capturedModifiers == 0) return;

            capturedKey = (uint)virtualKey;
            PopulateHotkeyKeyVisuals(keyDisplayPanel, capturedModifiers, capturedKey);
            instructionText.Text = "이 단축키를 사용하시겠습니까?";
            dialog.IsPrimaryButtonEnabled = true;
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || capturedKey == 0) return;

        SettingsManager.HotkeyModifiers = capturedModifiers;
        SettingsManager.HotkeyKey = capturedKey;
        PopulateHotkeyKeyVisuals(HotkeyDisplayPanel, capturedModifiers, capturedKey);

        if (SettingsManager.HotkeyEnabled)
            App.HotkeyManager.UpdateHotkey(capturedModifiers, capturedKey);
    }

    private static bool IsVirtualKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static void PopulateHotkeyKeyVisuals(Panel panel, uint modifiers, uint key)
    {
        panel.Children.Clear();
        bool isFirst = true;

        void AddKeyVisual(string name)
        {
            if (!isFirst)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "+",
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.6,
                    Margin = new Thickness(2, 0, 2, 0),
                });
            }
            panel.Children.Add(new KeyVisual
            {
                Content = name,
                VisualType = VisualType.Small,
            });
            isFirst = false;
        }

        if ((modifiers & HotkeyManager.ModifierControl) != 0) AddKeyVisual("Ctrl");
        if ((modifiers & HotkeyManager.ModifierAlt) != 0) AddKeyVisual("Alt");
        if ((modifiers & HotkeyManager.ModifierShift) != 0) AddKeyVisual("Shift");
        if ((modifiers & HotkeyManager.ModifierWin) != 0) AddKeyVisual("Win");
        AddKeyVisual(GetKeyDisplayName(key));
    }

    private static string GetKeyDisplayName(uint virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        0x20 => "Space",
        0x09 => "Tab",
        0x1B => "Esc",
        0x2E => "Del",
        0x2D => "Ins",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PgUp",
        0x22 => "PgDn",
        0xC0 => "`",
        0xBD => "-",
        0xBB => "=",
        0xDB => "[",
        0xDD => "]",
        0xDC => "\\",
        0xBA => ";",
        0xDE => "'",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        _ => $"0x{virtualKey:X2}",
    };

    private async Task<bool?> AskExportFavoritesAsync(
        IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> selectedPackageKeys = null)
    {
        if (!ContentsManager.HasFavorites(selectedPackageKeys)) return false;

        var result = await this.ShowDialogAsync(
            "즐겨찾기 내보내기",
            "즐겨찾기 정보가 존재합니다.\n즐겨찾기도 함께 내보내시겠습니까?",
            primaryButtonText: "예",
            secondaryButtonText: "아니오");

        if (result == ContentDialogResult.None) return null;

        return result == ContentDialogResult.Primary;
    }

    private async Task<bool> EnsureArcaconSynchronizationAvailableAsync()
    {
        if (!App.ArcaconClient.IsLoggedIn)
            return await ArcaconSessionHelper.ShowArcaconLoginDialogAsync(this);

        try
        {
            ManageWindow.ShowLoading("아카콘 로그인 상태를 확인하는 중...");
            await App.ArcaconClient.GetNewListAsync();
            ManageWindow.HideLoading();
            return true;
        }
        catch (Exception exception) when (exception is ArcaconLoginException or InvalidOperationException)
        {
            ManageWindow.HideLoading();
            return await ArcaconSessionHelper.ShowArcaconLoginDialogAsync(this);
        }
        catch
        {
            ManageWindow.HideLoading();
            throw;
        }
    }

    private async Task<bool?> AskIncludeInactiveArcaconsAsync()
    {
        var contentDialogResult = await this.ShowDialogAsync(
            "미사용 아카콘 포함",
            "현재 사용하지 않는 아카콘도 함께 동기화할까요?",
            primaryButtonText: "예",
            secondaryButtonText: "아니오");
        if (contentDialogResult == ContentDialogResult.None) return null;

        return contentDialogResult == ContentDialogResult.Primary;
    }

    private static async Task<int> SynchronizeArcaconSubscriptionsAsync(bool includeInactiveArcacons)
    {
        ManageWindow.ShowLoading("아카콘 구독 목록을 불러오는 중...");
        var subscribedPackages = await App.ArcaconClient.GetSubscribedPackagesAsync(includeInactiveArcacons);
        var packageIndexesToDownload = subscribedPackages
            .Where(package => !ContentsManager.IsPackageDownloaded(ContentSource.Arcacon, package.PackageIndex.ToString()))
            .Select(package => package.PackageIndex)
            .ToList();

        if (packageIndexesToDownload.Count == 0)
        {
            ManageWindow.HideLoading();
            return 0;
        }

        for (var packageOrder = 0; packageOrder < packageIndexesToDownload.Count; packageOrder++)
        {
            var packageIndex = packageIndexesToDownload[packageOrder];
            var currentPackageOrder = packageOrder + 1;

            ManageWindow.ShowLoading($"아카콘 동기화 중... {currentPackageOrder}/{packageIndexesToDownload.Count}");
            await ContentsManager.DownloadArcaconPackageAsync(packageIndex, new Progress<(int Completed, int Total)>(progress => ManageWindow.ShowLoading($"아카콘 동기화 중... {currentPackageOrder}/{packageIndexesToDownload.Count} ({progress.Completed}/{progress.Total})")));
        }

        ManageWindow.HideLoading();
        return packageIndexesToDownload.Count;
    }

    private static async Task<StorageFile> PickPackageExportFileAsync(string suggestedFileName)
    {
        var savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("YellowInside 패키지", [".yip"]);
        savePicker.SuggestedFileName = suggestedFileName;

        InitializeWithWindow.Initialize(savePicker, ManageWindow.Instance.GetWindowHandle());

        return await savePicker.PickSaveFileAsync();
    }

    private async Task ExportPackagesAsync(
        string destinationFilePath,
        string loadingMessage,
        IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> selectedPackageKeys = null,
        bool exportFavorites = true)
    {
        try
        {
            ManageWindow.ShowLoading(loadingMessage);
            if (selectedPackageKeys is null) await Task.Run(() => ContentsManager.ExportAsync(destinationFilePath, exportFavorites));
            else await Task.Run(() => ContentsManager.ExportAsync(destinationFilePath, selectedPackageKeys, exportFavorites));
            ManageWindow.HideLoading();

            await this.ShowDialogAsync("내보내기 완료", "패키지를 성공적으로 내보냈습니다.");
        }
        catch (Exception exception)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("내보내기 실패", exception.Message);
        }
    }

    private async Task<bool> ConfirmReplaceImportAsync(string sourceFilePath)
    {
        var importedPackages = await ContentsManager.ReadPackagesFromImportFileAsync(sourceFilePath);
        var importingKeySet = importedPackages.Select(package => (package.Source, package.PackageIdentifier)).ToHashSet();

        var deletedPackages = ContentsManager.GetDownloadedPackages()
            .Where(package => !importingKeySet.Contains((package.Source, package.PackageIdentifier)))
            .ToList();
        if (deletedPackages.Count == 0) return true;

        var replaceImportWarningDialog = new ReplaceImportWarningDialog(deletedPackages) { XamlRoot = XamlRoot, };
        var warningDialogResult = await replaceImportWarningDialog.ShowAsync();
        return warningDialogResult == ContentDialogResult.Primary;
    }
}
