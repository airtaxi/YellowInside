using YellowInside.Models;

namespace YellowInside.ViewModels;

public sealed class ReplaceImportWarningListViewModel(StickerPackage stickerPackage)
{
    public string Title { get; } = stickerPackage.Title;

    public string SourceAndSellerName { get; } = CreateSourceAndSellerName(stickerPackage);

    private static string CreateSourceAndSellerName(StickerPackage stickerPackage)
    {
        var sellerName = string.IsNullOrWhiteSpace(stickerPackage.SellerName) ? "제작자 정보 없음" : stickerPackage.SellerName;
        return $"{stickerPackage.Source.GetFriendlyName()} · {sellerName}";
    }
}
