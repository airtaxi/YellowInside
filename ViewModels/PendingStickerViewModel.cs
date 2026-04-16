using CommunityToolkit.Mvvm.Input;
using YellowInside.Models;
using Microsoft.UI.Xaml.Media;
using System;

namespace YellowInside.ViewModels;

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
