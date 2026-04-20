using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;
using WinUIEx;
using YellowInside.Models;

namespace YellowInside.Pages.Manage;

public enum CustomPackageEditorMode { Add, Edit }

public sealed class CustomPackageEditorArguments(CustomPackageEditorMode mode, string packageIdentifier = null)
{
    public CustomPackageEditorMode Mode { get; } = mode;
    public string PackageIdentifier { get; } = packageIdentifier;
}

public sealed class StickerFileItem
{
    public string SourceFilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public BitmapImage Thumbnail { get; set; }
    public bool IsExisting { get; set; }
    public string OriginalStickerPath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
}

public sealed partial class CustomPackageEditorPage : Page
{
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<StickerFileItem> StickerFiles { get; } = [];

    private string _mainImageFilePath = string.Empty;
    private bool _hasExistingMainImage;
    private CustomPackageEditorMode _mode = CustomPackageEditorMode.Add;
    private string _packageIdentifier;

    public CustomPackageEditorPage()
    {
        InitializeComponent();
        StickerFiles.CollectionChanged += (_, _) => UpdateStickerCountText();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is CustomPackageEditorArguments arguments)
        {
            _mode = arguments.Mode;
            _packageIdentifier = arguments.PackageIdentifier;
        }

        if (_mode == CustomPackageEditorMode.Edit && !string.IsNullOrEmpty(_packageIdentifier))
        {
            HeaderTextBlock.Text = "사용자 지정콘 수정";
            SaveButton.Content = "수정";
            await LoadExistingPackageAsync();
        }
        else
        {
            HeaderTextBlock.Text = "사용자 지정콘 추가";
            SaveButton.Content = "저장";
            RegistrationDateTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    private async Task LoadExistingPackageAsync()
    {
        var package = ContentsManager.GetDownloadedPackage(ContentSource.Local, _packageIdentifier);
        if (package is null) return;

        TitleTextBox.Text = package.Title;
        DescriptionTextBox.Text = package.Description;
        SellerNameTextBox.Text = package.SellerName;
        RegistrationDateTextBox.Text = package.RegistrationDate;

        foreach (var tag in package.Tags) Tags.Add(tag);

        if (!string.IsNullOrEmpty(package.MainImageFileName))
        {
            var mainImagePath = ContentsManager.GetMainImagePath(ContentSource.Local, _packageIdentifier, package.MainImageFileName);
            if (File.Exists(mainImagePath))
            {
                _hasExistingMainImage = true;
                MainImageFileNameTextBlock.Text = package.MainImageFileName;
                MainImagePreview.Source = await LoadBitmapWithoutLockingAsync(mainImagePath);
            }
        }

        foreach (var sticker in package.Stickers.OrderBy(sticker => sticker.SortNumber))
        {
            var stickerFilePath = ContentsManager.GetStickerImagePath(
                ContentSource.Local, _packageIdentifier, package.LocalDirectoryName, sticker.FileName);

            BitmapImage thumbnail = null;
            if (File.Exists(stickerFilePath))
                thumbnail = await LoadBitmapWithoutLockingAsync(stickerFilePath);

            StickerFiles.Add(new StickerFileItem
            {
                SourceFilePath = stickerFilePath,
                DisplayName = string.IsNullOrEmpty(sticker.Title) ? sticker.FileName : sticker.Title,
                Thumbnail = thumbnail,
                IsExisting = true,
                OriginalStickerPath = sticker.Path,
                OriginalFileName = sticker.FileName,
            });
        }

        UpdateSaveButtonState();
    }

    /// <summary>
    /// BitmapImage를 MemoryStream을 통해 로드하여 파일 락을 방지합니다.
    /// </summary>
    private static async Task<BitmapImage> LoadBitmapWithoutLockingAsync(string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmapImage = new BitmapImage();
        await bitmapImage.SetSourceAsync(stream);
        return bitmapImage;
    }

    private void UpdateSaveButtonState()
        => SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleTextBox.Text) && (_hasExistingMainImage || !string.IsNullOrEmpty(_mainImageFilePath));

    private void UpdateStickerCountText()
    {
        StickerCountTextBlock.Text = $"{StickerFiles.Count}개";
        NoStickersTextBlock.Visibility = StickerFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTitleTextBoxTextChanged(object sender, TextChangedEventArgs e)
        => UpdateSaveButtonState();

    private async void OnSelectMainImageButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".gif");
        openPicker.FileTypeFilter.Add(".webp");
        openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var file = await openPicker.PickSingleFileAsync();
        if (file is null) return;

        _mainImageFilePath = file.Path;
        MainImageFileNameTextBlock.Text = Path.GetFileName(file.Path);
        MainImagePreview.Source = new BitmapImage(new Uri(file.Path));
        UpdateSaveButtonState();
    }

    private async void OnAddStickersButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".gif");
        openPicker.FileTypeFilter.Add(".webp");
        openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var files = await openPicker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        foreach (var file in files)
        {
            if (StickerFiles.Any(item => item.SourceFilePath == file.Path)) continue;

            StickerFiles.Add(new StickerFileItem
            {
                SourceFilePath = file.Path,
                DisplayName = Path.GetFileNameWithoutExtension(file.Path),
                Thumbnail = new BitmapImage(new Uri(file.Path)),
            });
        }
    }

    private void OnRemoveStickerButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not StickerFileItem item) return;
        StickerFiles.Remove(item);
    }

    private void OnAddTagButtonClicked(object sender, RoutedEventArgs e) => AddCurrentTag();

    private void OnTagInputTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        AddCurrentTag();
        e.Handled = true;
    }

    private void AddCurrentTag()
    {
        var tag = TagInputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(tag) || Tags.Contains(tag)) return;

        Tags.Add(tag);
        TagInputTextBox.Text = string.Empty;
        TagInputTextBox.Focus(FocusState.Programmatic);
    }

    private void OnRemoveTagButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag) return;
        Tags.Remove(tag);
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void OnSaveButtonClicked(object sender, RoutedEventArgs e)
    {
        if (_mode == CustomPackageEditorMode.Edit)
        {
            ManageWindow.ShowLoading("사용자 지정콘 수정 중...");
            try
            {
                var stickerEntries = StickerFiles
                    .Select(item => (item.SourceFilePath, item.IsExisting, item.OriginalStickerPath, item.OriginalFileName))
                    .ToList();

                await ContentsManager.UpdateCustomPackageAsync(
                    _packageIdentifier,
                    TitleTextBox.Text.Trim(),
                    DescriptionTextBox.Text.Trim(),
                    _mainImageFilePath,
                    SellerNameTextBox.Text.Trim(),
                    [.. Tags],
                    stickerEntries);
            }
            finally { ManageWindow.HideLoading(); }
        }
        else
        {
            ManageWindow.ShowLoading("사용자 지정콘 추가 중...");
            try
            {
                var stickerSourcePaths = StickerFiles.Select(item => item.SourceFilePath).ToList();
                await ContentsManager.AddCustomPackageAsync(
                    TitleTextBox.Text.Trim(),
                    DescriptionTextBox.Text.Trim(),
                    _mainImageFilePath,
                    SellerNameTextBox.Text.Trim(),
                    RegistrationDateTextBox.Text,
                    [.. Tags],
                    stickerSourcePaths);
            }
            finally { ManageWindow.HideLoading(); }
        }

        if (Frame.CanGoBack) Frame.GoBack();
    }
}
