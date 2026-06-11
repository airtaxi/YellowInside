using CommunityToolkit.Mvvm.ComponentModel;
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
using Windows.Devices.PointOfService;
using Windows.Storage;

namespace YellowInside.ViewModels;

public partial class PopupViewModel : ObservableObject
{
    private const string SettingsKeySource = "PopupLastSource";
    private const string SettingsKeyPackageIdentifier = "PopupLastPackageIdentifier";
    private const string SettingsKeySpecialTab = "PopupLastSpecialTab";
    private const int FavoriteCategoryIndex = 0;
    private const int TagCategoryIndex = 1;
    private const int HistoryCategoryIndex = 2;
    private const int FirstPackageCategoryIndex = 3;
    private const int SpecialTabFavorite = 0;
    private const int SpecialTabHistory = 1;
    private const int SpecialTabTag = 2;

    private readonly List<StickerPackage> _packages;
    private readonly List<PopupStickerViewModel> _categoryStickers = [];
    private readonly Action<PopupStickerViewModel> _stickerClicked;
    private bool _isChangingCategory;
    private string _tagSearchText = string.Empty;
    private Visibility _tagSearchVisibility = Visibility.Collapsed;

    public List<PopupCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<PopupStickerViewModel> Stickers { get; } = [];
    public ObservableCollection<PendingStickerViewModel> PendingStickers { get; } = [];
    public ObservableCollection<string> TagSuggestions { get; } = [];
    public nint ChatHwnd { get; }
    public bool HasPackages => _packages.Count > 0;

    public const int MaxPendingCount = 30;
    public Visibility PendingBarVisibility => PendingStickers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string PendingCountText => $"{PendingStickers.Count}/{MaxPendingCount}";
    public Visibility TagSearchVisibility
    {
        get => _tagSearchVisibility;
        private set => SetProperty(ref _tagSearchVisibility, value);
    }

    public string TagSearchText
    {
        get => _tagSearchText;
        set
        {
            if (!SetProperty(ref _tagSearchText, value ?? string.Empty)) return;
            if (_isChangingCategory) return;

            ApplyTagSearch();
            UpdateTagSuggestions();
        }
    }

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
        var existing = PendingStickers.FirstOrDefault(pendingSticker => pendingSticker.LocalFilePath == sticker.LocalFilePath);

        if (existing is not null)
        {
            PendingStickers.Remove(existing);
            sticker.IsPending = false;
            return false;
        }

        if (PendingStickers.Count >= MaxPendingCount) return false;

        sticker.IsPending = true;
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

    public void RemoveFromPending(PendingStickerViewModel item)
    {
        PendingStickers.Remove(item);
        var matchingSticker = _categoryStickers.FirstOrDefault(sticker => sticker.LocalFilePath == item.LocalFilePath);
        if (matchingSticker is not null) matchingSticker.IsPending = false;
    }

    public void ClearPending()
    {
        foreach (var sticker in _categoryStickers) sticker.IsPending = false;
        PendingStickers.Clear();
    }

    public IReadOnlyList<string> GetPendingFilePaths() => PendingStickers.Select(pendingSticker => pendingSticker.LocalFilePath).ToList();

    public void UpdateTagSearchText(string text) => TagSearchText = text;

    private void BuildCategories()
    {
        Categories.Add(new PopupCategoryViewModel(true, default, default, default, default)
        {
            Title = "즐겨찾기",
        });

        Categories.Add(new PopupCategoryViewModel(default, true, default, default, default)
        {
            Title = "태그",
        });

        Categories.Add(new PopupCategoryViewModel(default, default, true, default, default)
        {
            Title = "최근 사용",
        });

        foreach (var package in _packages)
        {
            var mainImagePath = ContentsManager.GetMainImagePath(package.Source, package.PackageIdentifier, package.MainImageFileName);

            ImageSource thumbnailSource = null;
            if (!string.IsNullOrEmpty(mainImagePath) && File.Exists(mainImagePath)) thumbnailSource = new BitmapImage(new Uri(mainImagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled };

            Categories.Add(new PopupCategoryViewModel(false, default, default, thumbnailSource, package)
            {
                Title = package.Title,
            });
        }
    }

    public int GetInitialCategoryIndex()
    {
        var settings = ApplicationData.Current.LocalSettings;

        if (settings.Values.TryGetValue(SettingsKeySource, out var sourceObject) && settings.Values.TryGetValue(SettingsKeyPackageIdentifier, out var packageIdentifierObject) && sourceObject is int source)
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
                for (var index = FirstPackageCategoryIndex; index < Categories.Count; index++)
                {
                    var category = Categories[index];
                    if (category.Package is not null && (int)category.Package.Source == source && category.Package.PackageIdentifier == packageIdentifier) return index;
                }
            }
        }

        if (settings.Values.TryGetValue(SettingsKeySpecialTab, out var specialTabObject) && specialTabObject is int specialTab)
        {
            return specialTab switch
            {
                SpecialTabFavorite => FavoriteCategoryIndex,
                SpecialTabHistory => HistoryCategoryIndex,
                SpecialTabTag => TagCategoryIndex,
                _ => FavoriteCategoryIndex,
            };
        }

        return FavoriteCategoryIndex;
    }

    private int _currentCategoryIndex;

    public void SelectCategory(int index)
    {
        if (index < 0 || index >= Categories.Count) return;

        _isChangingCategory = true;
        _currentCategoryIndex = index;
        RememberCategory(index);
        TagSearchText = string.Empty;
        TagSuggestions.Clear();
        Stickers.Clear();
        _categoryStickers.Clear();

        if (index == FavoriteCategoryIndex) LoadFavoriteStickers();
        else if (index == TagCategoryIndex) LoadTaggedStickers();
        else if (index == HistoryCategoryIndex) LoadHistoryStickers();
        else LoadPackageStickers(Categories[index].Package);

        ApplyPendingFlags();
        RefreshTagSearchState();
        _isChangingCategory = false;
        ApplyTagSearch();
    }

    private void ApplyPendingFlags()
    {
        var pendingFilePaths = new HashSet<string>(PendingStickers.Select(pendingSticker => pendingSticker.LocalFilePath));
        foreach (var sticker in _categoryStickers) sticker.IsPending = pendingFilePaths.Contains(sticker.LocalFilePath);
    }

    private void RefreshTagSearchState()
    {
        TagSearchVisibility = _categoryStickers.Any(sticker => !string.IsNullOrWhiteSpace(sticker.Tag)) ? Visibility.Visible : Visibility.Collapsed;
        UpdateTagSuggestions();
    }

    private void ApplyTagSearch()
    {
        Stickers.Clear();

        foreach (var sticker in _categoryStickers.Where(MatchesTagSearch)) Stickers.Add(sticker);
    }

    private bool MatchesTagSearch(PopupStickerViewModel sticker)
    {
        var searchText = TagSearchText.Trim();
        if (string.IsNullOrEmpty(searchText)) return true;
        return !string.IsNullOrWhiteSpace(sticker.Tag) && sticker.Tag.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateTagSuggestions()
    {
        TagSuggestions.Clear();
        if (TagSearchVisibility != Visibility.Visible) return;

        var searchText = TagSearchText.Trim();
        var tags = _categoryStickers
            .Select(sticker => sticker.Tag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(tag => string.IsNullOrEmpty(searchText) || tag.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tag => tag)
            .Take(20);

        foreach (var tag in tags) TagSuggestions.Add(tag);
    }

    private void RememberCategory(int index)
    {
        var settings = ApplicationData.Current.LocalSettings;

        if (index >= FirstPackageCategoryIndex && index < Categories.Count && Categories[index].Package is { } package)
        {
            settings.Values[SettingsKeySource] = (int)package.Source;
            settings.Values[SettingsKeyPackageIdentifier] = package.PackageIdentifier;
            settings.Values.Remove(SettingsKeySpecialTab);
        }
        else
        {
            settings.Values.Remove(SettingsKeySource);
            settings.Values.Remove(SettingsKeyPackageIdentifier);
            settings.Values[SettingsKeySpecialTab] = index switch
            {
                TagCategoryIndex => SpecialTabTag,
                HistoryCategoryIndex => SpecialTabHistory,
                _ => SpecialTabFavorite,
            };
        }
    }

    private void LoadFavoriteStickers()
    {
        var favorites = ContentsManager.GetFavorites();
        foreach (var favorite in favorites)
        {
            var package = _packages.FirstOrDefault(package => package.Source == favorite.Source && package.PackageIdentifier == favorite.PackageIdentifier);
            if (package is null) continue;

            var sticker = package.Stickers.FirstOrDefault(sticker => sticker.Path == favorite.StickerPath);
            if (sticker is null) continue;

            var stickerViewModel = CreateStickerViewModel(package, sticker);
            if (stickerViewModel is null) continue;

            stickerViewModel.IsFavorite = true;
            _categoryStickers.Add(stickerViewModel);
        }
    }

    private void LoadTaggedStickers()
    {
        var taggedStickers = ContentsManager.GetTaggedStickers();
        foreach (var taggedSticker in taggedStickers)
        {
            var stickerViewModel = CreateStickerViewModel(taggedSticker.Package, taggedSticker.Sticker);
            if (stickerViewModel is null) continue;

            _categoryStickers.Add(stickerViewModel);
        }
    }

    private void LoadHistoryStickers()
    {
        var historyEntries = HistoryManager.GetEntries();
        foreach (var entry in historyEntries)
        {
            var package = _packages.FirstOrDefault(package => package.Source == entry.Source && package.PackageIdentifier == entry.PackageIdentifier);
            if (package is null) continue;

            var sticker = package.Stickers.FirstOrDefault(sticker => sticker.Path == entry.StickerPath);
            if (sticker is null) continue;

            var stickerViewModel = CreateStickerViewModel(package, sticker);
            if (stickerViewModel is null) continue;

            _categoryStickers.Add(stickerViewModel);
        }
    }

    private void LoadPackageStickers(StickerPackage package)
    {
        if (package is null) return;

        foreach (var sticker in package.Stickers)
        {
            var stickerViewModel = CreateStickerViewModel(package, sticker);
            if (stickerViewModel is null) continue;

            _categoryStickers.Add(stickerViewModel);
        }
    }

    private PopupStickerViewModel CreateStickerViewModel(StickerPackage package, Sticker sticker)
    {
        var imagePath = ContentsManager.GetStickerImagePath(package.Source, package.PackageIdentifier, package.LocalDirectoryName, sticker);
        if (!File.Exists(imagePath)) return null;

        return new PopupStickerViewModel
        {
            ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
            LocalFilePath = imagePath,
            Title = sticker.Title,
            Tag = sticker.Tag,
            Source = package.Source,
            PackageIdentifier = package.PackageIdentifier,
            StickerPath = sticker.Path,
            IsFavorite = ContentsManager.IsFavorite(package.Source, package.PackageIdentifier, sticker.Path),
            FavoriteToggled = OnFavoriteToggled,
            StickerClicked = _stickerClicked,
        };
    }

    private void OnFavoriteToggled(PopupStickerViewModel item)
    {
        // 즐겨찾기 탭에서 즐겨찾기 해제하면 목록에서 제거
        if (_currentCategoryIndex != FavoriteCategoryIndex || item.IsFavorite) return;

        _categoryStickers.Remove(item);
        Stickers.Remove(item);
        RefreshTagSearchState();
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
            {
                pendingBitmap.UriSource = null;
            }
        }
        PendingStickers.Clear();

        foreach (var sticker in _categoryStickers)
        {
            sticker.ImageSource = null;
            sticker.FavoriteToggled = null;
            sticker.StickerClicked = null;
        }
        _categoryStickers.Clear();
        Stickers.Clear();
        TagSuggestions.Clear();

        foreach (var category in Categories)
        {
            if (category.ThumbnailSource is BitmapImage bitmap)
            {
                bitmap.UriSource = null;
            }
        }
        Categories.Clear();
    }
}
