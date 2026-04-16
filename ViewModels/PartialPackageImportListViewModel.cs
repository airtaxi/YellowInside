using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Threading.Tasks;
using YellowInside.Models;

namespace YellowInside.ViewModels;

public sealed class PartialPackageImportListViewModel : PackageSelectionListViewModel
{
    private PartialPackageImportListViewModel(StickerPackage stickerPackage, BitmapImage thumbnailSource) : base(stickerPackage, thumbnailSource) { }

    public static async Task<PartialPackageImportListViewModel> CreateAsync(StickerPackage stickerPackage, string temporaryDirectory)
    {
        var thumbnailSource = await CreateThumbnailSourceAsync(stickerPackage, temporaryDirectory);
        return new PartialPackageImportListViewModel(stickerPackage, thumbnailSource);
    }

    private static async Task<BitmapImage> CreateThumbnailSourceAsync(StickerPackage stickerPackage, string temporaryDirectory)
    {
        if (string.IsNullOrWhiteSpace(stickerPackage.MainImageFileName)) return null;

        var mainImagePath = Path.Combine(
            temporaryDirectory,
            stickerPackage.Source.ToString(),
            stickerPackage.PackageIdentifier,
            stickerPackage.MainImageFileName);
        if (!File.Exists(mainImagePath)) return null;

        var bitmapImage = new BitmapImage { AutoPlay = SettingsManager.GifPlaybackEnabled };
        using var stream = new MemoryStream(await File.ReadAllBytesAsync(mainImagePath));
        await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
        return bitmapImage;
    }
}
