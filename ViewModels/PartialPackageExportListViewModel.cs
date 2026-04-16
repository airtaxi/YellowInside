using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YellowInside.Models;

namespace YellowInside.ViewModels;

public sealed class PartialPackageExportListViewModel(StickerPackage stickerPackage)
{
    public ContentSource Source { get; } = stickerPackage.Source;

    public string PackageIdentifier { get; } = stickerPackage.PackageIdentifier;

    public string Title { get; } = stickerPackage.Title;

    public string SellerName { get; } = string.IsNullOrWhiteSpace(stickerPackage.SellerName) ? "제작자 정보 없음" : stickerPackage.SellerName;

    public BitmapImage ThumbnailSource { get; } = CreateThumbnailSource(stickerPackage);

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
