using Microsoft.UI.Xaml.Media.Imaging;
using YellowInside.Models;

namespace YellowInside.ViewModels;

public class PackageSelectionListViewModel(StickerPackage stickerPackage, BitmapImage thumbnailSource)
{
    public ContentSource Source { get; } = stickerPackage.Source;

    public string PackageIdentifier { get; } = stickerPackage.PackageIdentifier;

    public string Title { get; } = stickerPackage.Title;

    public string SellerName { get; } = string.IsNullOrWhiteSpace(stickerPackage.SellerName) ? "제작자 정보 없음" : stickerPackage.SellerName;

    public BitmapImage ThumbnailSource { get; } = thumbnailSource;
}
