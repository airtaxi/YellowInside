using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YellowInside.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.PointOfService;
using Windows.Storage;

namespace YellowInside.ViewModels;

public class PopupCategoryViewModel
{
    public bool IsFavorite { get; init; }
    public string Title { get; init; } = string.Empty;
    public ImageSource ThumbnailSource { get; init; }
    public StickerPackage Package { get; init; }
    public Visibility FavoriteIconVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThumbnailVisibility => IsFavorite ? Visibility.Collapsed : Visibility.Visible;
}

public partial class PopupStickerViewModel : ObservableObject
{
    public ImageSource ImageSource { get; set; }
    public string LocalFilePath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public ContentSource Source { get; init; }
    public int PackageIndex { get; init; }
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
        if (IsFavorite) await ContentsManager.RemoveFavoriteAsync(Source, PackageIndex, StickerPath);
        else await ContentsManager.AddFavoriteAsync(Source, PackageIndex, StickerPath);

        IsFavorite = !IsFavorite;
        FavoriteToggled?.Invoke(this);
    }

    [RelayCommand]
    public void Send() => StickerClicked?.Invoke(this);
}

public partial class PopupViewModel : ObservableObject
{
    private const string SettingsKeySource = "PopupLastSource";
    private const string SettingsKeyPackageIndex = "PopupLastPackageIndex";

    private readonly List<StickerPackage> _packages;
    private readonly Action<PopupStickerViewModel> _stickerClicked;

    public List<PopupCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<PopupStickerViewModel> Stickers { get; } = [];
    public nint ChatHwnd { get; }

    public PopupViewModel(nint chatHwnd, Action<PopupStickerViewModel> stickerClicked)
    {
        ChatHwnd = chatHwnd;
        _stickerClicked = stickerClicked;
        _packages = [.. ContentsManager.GetDownloadedPackages()];
        BuildCategories();
    }

    private void BuildCategories()
    {
        Categories.Add(new PopupCategoryViewModel
        {
            IsFavorite = true,
            Title = "즐겨찾기",
        });

        foreach (var package in _packages)
        {
            var mainImagePath = ContentsManager.GetMainImagePath(
                package.Source, package.PackageIndex, package.MainImageFileName);

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
            settings.Values.TryGetValue(SettingsKeyPackageIndex, out var packageIndexObject) &&
            sourceObject is int source && packageIndexObject is int packageIndex)
        {
            for (int i = 1; i < Categories.Count; i++)
            {
                var category = Categories[i];
                if (category.Package is not null &&
                    (int)category.Package.Source == source &&
                    category.Package.PackageIndex == packageIndex)
                    return i;
            }
        }

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
        else
            LoadPackageStickers(Categories[index].Package);
    }

    private void RememberCategory(int index)
    {
        var settings = ApplicationData.Current.LocalSettings;

        if (index > 0 && index < Categories.Count && Categories[index].Package is { } package)
        {
            settings.Values[SettingsKeySource] = (int)package.Source;
            settings.Values[SettingsKeyPackageIndex] = package.PackageIndex;
        }
        else
        {
            settings.Values.Remove(SettingsKeySource);
            settings.Values.Remove(SettingsKeyPackageIndex);
        }
    }

    private void LoadFavoriteStickers()
    {
        var favorites = ContentsManager.GetFavorites();
        foreach (var favorite in favorites)
        {
            var package = _packages.FirstOrDefault(
                package => package.Source == favorite.Source && package.PackageIndex == favorite.PackageIndex);
            if (package is null) continue;

            var sticker = package.Stickers.FirstOrDefault(sticker => sticker.Path == favorite.StickerPath);
            if (sticker is null) continue;

            var imagePath = ContentsManager.GetStickerImagePath(
                favorite.Source, favorite.PackageIndex, package.LocalDirectoryName, sticker.FileName);
            if (!File.Exists(imagePath)) continue;

            Stickers.Add(new PopupStickerViewModel
            {
                ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
                LocalFilePath = imagePath,
                Title = sticker.Title,
                Source = favorite.Source,
                PackageIndex = favorite.PackageIndex,
                StickerPath = favorite.StickerPath,
                IsFavorite = true,
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
                package.Source, package.PackageIndex, package.LocalDirectoryName, sticker.FileName);
            if (!File.Exists(imagePath)) continue;

            Stickers.Add(new PopupStickerViewModel
            {
                ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled },
                LocalFilePath = imagePath,
                Title = sticker.Title,
                Source = package.Source,
                PackageIndex = package.PackageIndex,
                StickerPath = sticker.Path,
                IsFavorite = ContentsManager.IsFavorite(package.Source, package.PackageIndex, sticker.Path),
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

    public void Cleanup()
    {
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
