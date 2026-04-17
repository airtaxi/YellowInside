using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using dccon.NET.Models;
using InvenSticker.NET.Models;
using YellowInside.Messages;
using YellowInside.Models;
using YellowInside.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace YellowInside.ViewModels;

public partial class SearchResultViewModel : ObservableObject
{
    public ContentSource Source { get; }

    public string PackageIdentifier { get; }

    public string Title { get; }

    public string SellerName { get; }

    [ObservableProperty]
    public partial ImageSource ThumbnailSource { get; private set; }

    [ObservableProperty]
    public partial ObservableCollection<string> Tags { get; private set; }

    public SearchResultViewModel(DcconPackageSummary dcconPackageSummary, CancellationToken cancellationToken = default)
    {
        Source = ContentSource.Dccon;
        PackageIdentifier = dcconPackageSummary.PackageIndex.ToString();
        Title = dcconPackageSummary.Title;
        SellerName = dcconPackageSummary.SellerName;
        UpdateFavoriteAndSubscriptionStatus();
        FetchThumbnail(dcconPackageSummary.ThumbnailUrl, cancellationToken);

        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    public SearchResultViewModel(InvenStickerPackageSummary stickerPackageSummary, CancellationToken cancellationToken = default)
    {
        Source = ContentSource.Inven;
        PackageIdentifier = stickerPackageSummary.PackageId.ToString();
        Title = stickerPackageSummary.Title;
        SellerName = stickerPackageSummary.AuthorName;
        UpdateFavoriteAndSubscriptionStatus();
        FetchThumbnail(stickerPackageSummary.ThumbnailUrl, cancellationToken);

        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    private async void FetchThumbnail(string url, CancellationToken cancellationToken)
    {
        try
        {
            if (Source == ContentSource.Dccon)
            {
                using var httpClient = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Referer", "https://dccon.dcinside.com");

                var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                cancellationToken.ThrowIfCancellationRequested();

                var bitmapImage = new BitmapImage() { AutoPlay = SettingsManager.GifPlaybackEnabled };
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                ThumbnailSource = bitmapImage;
            }
            else if (Source == ContentSource.Inven)
            {
                var bitmapImage = new BitmapImage(new Uri(url)) { AutoPlay = SettingsManager.GifPlaybackEnabled };
                ThumbnailSource = bitmapImage;
            }
        }
        catch { }
    }

    private void UpdateFavoriteAndSubscriptionStatus()
    {
        var subscriptions = ContentsManager.GetDownloadedPackages(Source);
        var favorites = ContentsManager.GetFavorites(Source);

        var isSubscribed = subscriptions.Any(package => package.PackageIdentifier == PackageIdentifier);
        var isFavorite = favorites.Any(favorite => favorite.PackageIdentifier == PackageIdentifier);

        var tags = new ObservableCollection<string>();
        if (isSubscribed) tags.Add("구독중");
        if (isFavorite) tags.Add("즐겨찾기 있음");

        Tags = tags;
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        if (message.Source != Source || message.Value != PackageIdentifier) return;

        UpdateFavoriteAndSubscriptionStatus();
    }

    public void OnClicked(object _, RoutedEventArgs __) => ManageWindow.Navigate(typeof(DetailPage), (Source, PackageIdentifier));
}
