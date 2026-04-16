using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YellowInside.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace YellowInside.ViewModels;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingBorderVisibility))]
    public partial bool IsPending { get; set; }

    public Visibility FavoriteIconVisibility => IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PendingBorderVisibility => IsPending ? Visibility.Visible : Visibility.Collapsed;

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
