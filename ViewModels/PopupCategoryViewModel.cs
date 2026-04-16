using YellowInside.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace YellowInside.ViewModels;

public record PopupCategoryViewModel(bool IsFavorite, bool IsHistory, ImageSource ThumbnailSource, StickerPackage Package)
{
    public string Title { get; init; } = string.Empty;

    public Visibility FavoriteIconVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HistoryIconVisibility => IsHistory ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThumbnailVisibility => !IsFavorite && !IsHistory ? Visibility.Visible : Visibility.Collapsed;
}
