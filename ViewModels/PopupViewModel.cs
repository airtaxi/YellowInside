using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YellowInside.Managers;
using YellowInside.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.PointOfService;
using Windows.Storage;

namespace YellowInside.ViewModels;

public class PopupCategoryViewModel
{
    public bool IsFavorite { get; init; }
    public bool IsHistory { get; init; }
    public string Title { get; init; } = string.Empty;
    public ImageSource ThumbnailSource { get; init; }
    public StickerPackage Package { get; init; }
    public Visibility FavoriteIconVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HistoryIconVisibility => IsHistory ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThumbnailVisibility => !IsFavorite && !IsHistory ? Visibility.Visible : Visibility.Collapsed;
}

public partial class PopupStickerViewModel : ObservableObject
{
    public ImageSource ImageSource { get; set; }
    public string LocalFilePath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public ContentSource Source { get; init; }
    public string PackageIdentifier { get; init; } = string.Empty;
    public string StickerPath { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIconVisibility))]
    public partial bool IsFavorite { get; set; }

    public Visibility FavoriteIconVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;

    public Action<PopupStickerViewModel> FavoriteToggled { get; set; }
    public Action<PopupStickerViewModel> StickerClicked { get; set; }

    [RelayCommand]
    public async Task ToggleFavorite()
    {
        if (IsFavorite) await ContentsManager.RemoveFavoriteAsync(Source, PackageIdentifier, StickerPath);
        else await ContentsManager.AddFavoriteAsync(Source, PackageIdentifier, StickerPath);

        IsFavorite = !IsFavorite;
        FavoriteToggled?.Invoke(this);
    }

    [RelayCommand]
    public void Send() => StickerClicked?.Invoke(this);
}

public partial class PendingStickerViewModel
{
    public string LocalFilePath { get; init; } = string.Empty;
    public ImageSource ImageSource { get; init; }
    public string Title { get; init; } = string.Empty;
    public ContentSource Source { get; init; }
    public string PackageIdentifier { get; init; } = string.Empty;
    public string StickerPath { get; init; } = string.Empty;
    public Action<PendingStickerViewModel> RemoveAction { get; init; }

    [RelayCommand]
    private void Remove() => RemoveAction?.Invoke(this);
}

public partial class PopupViewModel : ObservableObject
{
    private const string SettingsKeySource = "PopupLastSource";
    private const string SettingsKeyPackageIdentifier = "PopupLastPackageIdentifier";
    private const string SettingsKeySpecialTab = "PopupLastSpecialTab";

    private readonly List<StickerPackage> _packages;
    private readonly Action<PopupStickerViewModel> _stickerClicked;

    public List<PopupCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<PopupStickerViewModel> Stickers { get; } = [];
    public ObservableCollection<PendingStickerViewModel> PendingStickers { get; } = [];
    public nint ChatHwnd { get; }
    public bool HasPackages => _packages.Count > 0;

    public const int MaxPendingCount = 30;
    public Visibility PendingBarVisibility => PendingStickers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string PendingCountText => $"{PendingStickers.Count}/{MaxPendingCount}";

    public PopupViewModel(nint chatHwnd, Action<PopupStickerViewModel> stickerClicked)
    {
        ChatHwnd = chatHwnd;
        _stickerClicked = stickerClicked;
        _packages = [.. ContentsManager.GetDownloadedPackages()];
        PendingStickers.CollectionChanged += OnPendingStickersCollectionChanged;
        BuildCategories();
    }

    private void OnPendingStickersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PendingBarVisibility));
        OnPropertyChanged(nameof(PendingCountText));
    }

    public bool TogglePending(PopupStickerViewModel sticker)
    {
        var existing = PendingStickers.FirstOrDefault(
            pendingSticker => pendingSticker.LocalFilePath == sticker.LocalFilePath);

        if (existing is not null)
        {
            PendingStickers.Remove(existing);
            return false;
        }

        if (PendingStickers.Count >= MaxPendingCount) return false;

        PendingStickers.Add(new PendingStickerViewModel
        {
            LocalFilePath = sticker.LocalFilePath,
            ImageSource = new BitmapImage(new Uri(sticker.LocalFilePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
            Title = sticker.Title,
            Source = sticker.Source,
            PackageIdentifier = sticker.PackageIdentifier,
            StickerPath = sticker.StickerPath,
            RemoveAction = RemoveFromPending,
        });
        return true;
    }

    public void RemoveFromPending(PendingStickerViewModel item) => PendingStickers.Remove(item);

    public void ClearPending() => PendingStickers.Clear();

    public IReadOnlyList<string> GetPendingFilePaths() =>
        PendingStickers.Select(pendingSticker => pendingSticker.LocalFilePath).ToList();

    private void BuildCategories()
    {
        Categories.Add(new PopupCategoryViewModel
        {
            IsFavorite = true,
            Title = "즐겨찾기",
        });

        Categories.Add(new PopupCategoryViewModel
        {
            IsHistory = true,
            Title = "최근 사용",
        });

        foreach (var package in _packages)
        {
            var mainImagePath = ContentsManager.GetMainImagePath(
                package.Source, package.PackageIdentifier, package.MainImageFileName);

            ImageSource thumbnailSource = null;
            if (!string.IsNullOrEmpty(mainImagePath) && File.Exists(mainImagePath))
                thumbnailSource = new BitmapImage(new Uri(mainImagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled };

            Categories.Add(new PopupCategoryViewModel
            {
                IsFavorite = false,
                Title = package.Title,
                ThumbnailSource = thumbnailSource,
                Package = package,
            });
        }
    }

    public int GetInitialCategoryIndex()
    {
        var settings = ApplicationData.Current.LocalSettings;

        if (settings.Values.TryGetValue(SettingsKeySource, out var sourceObject) &&
            settings.Values.TryGetValue(SettingsKeyPackageIdentifier, out var packageIdentifierObject) &&
            sourceObject is int source)
        {
            // LocalSettings 하위 호환: 기존 int 또는 새 string 모두 지원
            string packageIdentifier = packageIdentifierObject switch
            {
                string stringValue => stringValue,
                int intValue => intValue.ToString(),
                _ => null,
            };

            if (packageIdentifier is not null)
            {
                for (int i = 2; i < Categories.Count; i++)
                {
                    var category = Categories[i];
                    if (category.Package is not null &&
                        (int)category.Package.Source == source &&
                        category.Package.PackageIdentifier == packageIdentifier)
                        return i;
                }
            }
        }

        if (settings.Values.TryGetValue(SettingsKeySpecialTab, out var specialTabObject) &&
            specialTabObject is int specialTab && specialTab >= 0 && specialTab <= 1)
            return specialTab;

        return 0;
    }

    private int _currentCategoryIndex;

    public void SelectCategory(int index)
    {
        if (index < 0 || index >= Categories.Count) return;

        _currentCategoryIndex = index;
        RememberCategory(index);
        Stickers.Clear();

        if (index == 0)
            LoadFavoriteStickers();
        else if (index == 1)
            LoadHistoryStickers();
        else
            LoadPackageStickers(Categories[index].Package);
    }

    private void RememberCategory(int index)
    {
        var settings = ApplicationData.Current.LocalSettings;

        if (index > 1 && index < Categories.Count && Categories[index].Package is { } package)
        {
            settings.Values[SettingsKeySource] = (int)package.Source;
            settings.Values[SettingsKeyPackageIdentifier] = package.PackageIdentifier;
            settings.Values.Remove(SettingsKeySpecialTab);
        }
        else
        {
            settings.Values.Remove(SettingsKeySource);
            settings.Values.Remove(SettingsKeyPackageIdentifier);
            settings.Values[SettingsKeySpecialTab] = index;
        }
    }

    private void LoadFavoriteStickers()
    {
        var favorites = ContentsManager.GetFavorites();
        foreach (var favorite in favorites)
        {
            var package = _packages.FirstOrDefault(
                package => package.Source == favorite.Source && package.PackageIdentifier == favorite.PackageIdentifier);
            if (package is null) continue;

            var sticker = package.Stickers.FirstOrDefault(sticker => sticker.Path == favorite.StickerPath);
            if (sticker is null) continue;

            var imagePath = ContentsManager.GetStickerImagePath(
                favorite.Source, favorite.PackageIdentifier, package.LocalDirectoryName, sticker.FileName);
            if (!File.Exists(imagePath)) continue;

            Stickers.Add(new PopupStickerViewModel
            {
                ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
                LocalFilePath = imagePath,
                Title = sticker.Title,
                Source = favorite.Source,
                PackageIdentifier = favorite.PackageIdentifier,
                StickerPath = favorite.StickerPath,
                IsFavorite = true,
                FavoriteToggled = OnFavoriteToggled,
                StickerClicked = _stickerClicked,
            });
        }
    }

    private void LoadHistoryStickers()
    {
        var historyEntries = HistoryManager.GetEntries();
        foreach (var entry in historyEntries)
        {
            var package = _packages.FirstOrDefault(
                package => package.Source == entry.Source && package.PackageIdentifier == entry.PackageIdentifier);
            if (package is null) continue;

            var sticker = package.Stickers.FirstOrDefault(sticker => sticker.Path == entry.StickerPath);
            if (sticker is null) continue;

            var imagePath = ContentsManager.GetStickerImagePath(
                entry.Source, entry.PackageIdentifier, package.LocalDirectoryName, sticker.FileName);
            if (!File.Exists(imagePath)) continue;

            Stickers.Add(new PopupStickerViewModel
            {
                ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
                LocalFilePath = imagePath,
                Title = sticker.Title,
                Source = entry.Source,
                PackageIdentifier = entry.PackageIdentifier,
                StickerPath = entry.StickerPath,
                IsFavorite = ContentsManager.IsFavorite(entry.Source, entry.PackageIdentifier, entry.StickerPath),
                FavoriteToggled = OnFavoriteToggled,
                StickerClicked = _stickerClicked,
            });
        }
    }

    private void LoadPackageStickers(StickerPackage package)
    {
        foreach (var sticker in package.Stickers)
        {
            var imagePath = ContentsManager.GetStickerImagePath(
                package.Source, package.PackageIdentifier, package.LocalDirectoryName, sticker.FileName);
            if (!File.Exists(imagePath)) continue;

            Stickers.Add(new PopupStickerViewModel
            {
                ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
                LocalFilePath = imagePath,
                Title = sticker.Title,
                Source = package.Source,
                PackageIdentifier = package.PackageIdentifier,
                StickerPath = sticker.Path,
                IsFavorite = ContentsManager.IsFavorite(package.Source, package.PackageIdentifier, sticker.Path),
                FavoriteToggled = OnFavoriteToggled,
                StickerClicked = _stickerClicked,
            });
        }
    }

    private void OnFavoriteToggled(PopupStickerViewModel item)
    {
        // 즐겨찾기 탭에서 즐겨찾기 해제하면 목록에서 제거
        if (_currentCategoryIndex == 0 && !item.IsFavorite)
            Stickers.Remove(item);
    }

    public void RecordPendingHistory()
    {
        for (int i = PendingStickers.Count - 1; i >= 0; i--)
        {
            var pendingSticker = PendingStickers[i];
            HistoryManager.Record(pendingSticker.Source, pendingSticker.PackageIdentifier, pendingSticker.StickerPath);
        }
    }

    public void Cleanup()
    {
        PendingStickers.CollectionChanged -= OnPendingStickersCollectionChanged;

        foreach (var pendingSticker in PendingStickers)
        {
            if (pendingSticker.ImageSource is BitmapImage pendingBitmap)
                pendingBitmap.UriSource = null;
        }
        PendingStickers.Clear();

        foreach (var sticker in Stickers)
        {
            sticker.ImageSource = null;
            sticker.FavoriteToggled = null;
            sticker.StickerClicked = null;
        }
        Stickers.Clear();

        foreach (var category in Categories)
        {
            if (category.ThumbnailSource is BitmapImage bitmap)
                bitmap.UriSource = null;
        }
        Categories.Clear();
    }
}
