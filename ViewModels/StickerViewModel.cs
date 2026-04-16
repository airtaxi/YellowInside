using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using dccon.NET.Models;
using YellowInside.Messages;
using YellowInside.Models;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace YellowInside.ViewModels;

public partial class StickerViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ImageSource ImageSource { get; private set; }

    [ObservableProperty]
    public partial bool IsSubscribed { get; private set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; private set; }

    private readonly string _packageIdentifier;
    private readonly ContentSource _source;
    private readonly string _path;

    public StickerViewModel(string packageIdentifier, DcconSticker sticker)
    {
        _packageIdentifier = packageIdentifier;
        _source = ContentSource.Dccon;
        _path = sticker.Path;

        UpdateFavoriteAndSubscriptionStatus();
        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    public StickerViewModel(ContentSource source, string packageIdentifier, Models.Sticker sticker)
    {
        _packageIdentifier = packageIdentifier;
        _source = source;
        _path = sticker.Path;

        UpdateFavoriteAndSubscriptionStatus();
        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    public async Task FetchImageAsync()
    {
        // If the package is subscribed, we can get the image from local storage. Otherwise, we need to fetch it from the web.
        if (IsSubscribed)
        {
            var package = ContentsManager.GetDownloadedPackage(_source, _packageIdentifier);
            if (package != null)
            {
                var sticker = package.Stickers.FirstOrDefault(x => x.Path == _path);
                var imagePath = ContentsManager.GetStickerImagePath(_source, _packageIdentifier, package.LocalDirectoryName, sticker.FileName);
                ManageWindow.Instance.DispatcherQueue.TryEnqueue(() => { ImageSource = new BitmapImage(new Uri(imagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled }; });
                return;
            }
        }

        var taskCompletionSource = new TaskCompletionSource();
        ManageWindow.Instance.DispatcherQueue.TryEnqueue(async () =>
        {
            ImageSource = await Utils.GenerateImageSourceAsync(ManageWindow.Instance.DispatcherQueue, _source, Utils.GetImageUrl(_source, _path));
            taskCompletionSource.SetResult();
        });
        await taskCompletionSource.Task;
    }

    private void UpdateFavoriteAndSubscriptionStatus()
    {
        IsSubscribed = ContentsManager.IsPackageDownloaded(_source, _packageIdentifier);
        IsFavorite = IsSubscribed && ContentsManager.IsFavorite(_source, _packageIdentifier, _path);
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        if (message.Source != _source || message.Value != _packageIdentifier) return;

        UpdateFavoriteAndSubscriptionStatus();
    }

    public async void OnFavoriteSymbolIconTapped(object sender, TappedRoutedEventArgs args)
    {
        if (!IsSubscribed) return;

        if (IsFavorite) await ContentsManager.RemoveFavoriteAsync(_source, _packageIdentifier, _path);
        else await ContentsManager.AddFavoriteAsync(_source, _packageIdentifier, _path);
    }
}
