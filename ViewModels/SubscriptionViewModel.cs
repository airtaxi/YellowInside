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

public partial class SubscriptionViewModel : ObservableObject
{
    public ContentSource Source { get; }

    public string PackageIdentifier { get; }

    public string Title { get; }

    public string SellerName { get; }

    [ObservableProperty]
    public partial ImageSource ThumbnailSource { get; private set; }

    [ObservableProperty]
    public partial ObservableCollection<string> Tags { get; private set; }

    public SubscriptionViewModel(StickerPackage package)
    {
        Source = package.Source;
        PackageIdentifier = package.PackageIdentifier;
        Title = package.Title;
        SellerName = package.SellerName;
        Tags = [.. package.Tags];

        if (!string.IsNullOrEmpty(package.MainImageFileName))
        {
            var mainImagePath = ContentsManager.GetMainImagePath(Source, PackageIdentifier, package.MainImageFileName);
            if (File.Exists(mainImagePath))
                ThumbnailSource = new BitmapImage(new Uri(mainImagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled };
        }
    }

    public void OnClicked(object _, RoutedEventArgs __) => ManageWindow.Navigate(typeof(DetailPage), (Source, PackageIdentifier));
}
