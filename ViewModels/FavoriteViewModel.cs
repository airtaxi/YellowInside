using AngleSharp.Dom;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using dccon.NET.Models;
using YellowInside.Messages;
using YellowInside.Models;
using YellowInside.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace YellowInside.ViewModels;

public partial class FavoriteViewModel : ObservableObject
{
    public ContentSource Source { get; }

    public string PackageIdentifier { get; }

    public string Title { get; }

    public string SellerName { get; }

    [ObservableProperty]
    public partial ImageSource ThumbnailSource { get; private set; }

    [ObservableProperty]
    public partial ObservableCollection<string> Tags { get; private set; }

    public FavoriteViewModel(StickerPackage package)
    {
        Source = package.Source;
        PackageIdentifier = package.PackageIdentifier;
        Title = package.Title;
        SellerName = package.SellerName;
        ThumbnailSource = new BitmapImage(new Uri(ContentsManager.GetMainImagePath(Source, PackageIdentifier, package.MainImageFileName))) { AutoPlay = SettingsManager.GifPlaybackEnabled };
        UpdateTags();

        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    private void UpdateTags()
    {
        var favoriteCount = ContentsManager.GetFavorites(Source).Count(x => x.PackageIdentifier == PackageIdentifier);
        Tags = [$"즐겨찾기 {favoriteCount}개"];
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message) => UpdateTags();

    public void OnClicked(object _, RoutedEventArgs __) => ManageWindow.Navigate(typeof(DetailPage), (Source, PackageIdentifier));
}
