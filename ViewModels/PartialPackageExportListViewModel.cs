using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using YellowInside.Models;

namespace YellowInside.ViewModels;

public sealed class PartialPackageExportListViewModel(StickerPackage stickerPackage) : PackageSelectionListViewModel(stickerPackage, CreateThumbnailSource(stickerPackage))
{
    private static BitmapImage CreateThumbnailSource(StickerPackage stickerPackage)
    {
        if (string.IsNullOrWhiteSpace(stickerPackage.MainImageFileName)) return null;

        var mainImagePath = ContentsManager.GetMainImagePath(
            stickerPackage.Source,
            stickerPackage.PackageIdentifier,
            stickerPackage.MainImageFileName);
        if (!File.Exists(mainImagePath)) return null;

        return new BitmapImage(new Uri(mainImagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled };
    }
}
