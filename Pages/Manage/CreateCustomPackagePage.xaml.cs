using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using WinUIEx;

namespace YellowInside.Pages.Manage;

public sealed class StickerFileItem
{
    public string SourceFilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public BitmapImage Thumbnail { get; set; }
}

public sealed partial class CreateCustomPackagePage : Page
{
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<StickerFileItem> StickerFiles { get; } = [];

    private string _mainImageFilePath = string.Empty;

    public CreateCustomPackagePage()
    {
        InitializeComponent();
        RegistrationDateTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        StickerFiles.CollectionChanged += (_, _) => UpdateStickerCountText();
    }

    private void UpdateSaveButtonState()
        => SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleTextBox.Text) && !string.IsNullOrEmpty(_mainImageFilePath);

    private void UpdateStickerCountText()
    {
        StickerCountTextBlock.Text = $"{StickerFiles.Count}개";
        NoStickersTextBlock.Visibility = StickerFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTitleTextBoxTextChanged(object sender, TextChangedEventArgs e)
        => UpdateSaveButtonState();

    private async void OnSelectMainImageButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".gif");
        openPicker.FileTypeFilter.Add(".webp");
        openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        InitializeWithWindow.Initialize(openPicker, ManageWindow.Instance.GetWindowHandle());

        var file = await openPicker.PickSingleFileAsync();
        if (file is null) return;

        _mainImageFilePath = file.Path;
        MainImageFileNameTextBlock.Text = file.Name;
        MainImagePreview.Source = new BitmapImage(new Uri(file.Path));
        UpdateSaveButtonState();
    }

    private async void OnAddStickersButtonClicked(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        openPicker.FileTypeFilter.Add(".gif");
        openPicker.FileTypeFilter.Add(".webp");
        openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        InitializeWithWindow.Initialize(openPicker, ManageWindow.Instance.GetWindowHandle());

        var files = await openPicker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        foreach (var file in files)
        {
            if (StickerFiles.Any(item => item.SourceFilePath == file.Path)) continue;

            StickerFiles.Add(new StickerFileItem
            {
                SourceFilePath = file.Path,
                DisplayName = file.Name,
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

        if (Frame.CanGoBack) Frame.GoBack();
    }
}
