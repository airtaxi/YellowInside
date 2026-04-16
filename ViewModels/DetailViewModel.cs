using ABI.System;
using AngleSharp.Dom;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using dccon.NET.Models;
using YellowInside.Helpers;
using YellowInside.Messages;
using YellowInside.Models;
using YellowInside.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography.Core;
using Windows.Services.Maps;
using Windows.UI.WebUI;
using Uri = System.Uri;

namespace YellowInside.ViewModels;

public partial class DetailViewModel : ObservableObject
{
    public ContentSource Source { get; private set; }

    public string PackageIdentifier { get; private set; }

    [ObservableProperty]
    public partial string HeaderText { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string Description { get; private set; }

    [ObservableProperty]
    public partial string SellerName { get; private set; }

    [ObservableProperty]
    public partial string IconCount { get; private set; }

    [ObservableProperty]
    public partial string SaleCount { get; private set; }

    [ObservableProperty]
    public partial string RegisterationDate { get; private set; }

    [ObservableProperty]
    public partial List<string> Tags { get; private set; }

    [ObservableProperty]
    public partial ImageSource MainImageSource { get; private set; }

    [ObservableProperty]
    public partial List<StickerViewModel> Stickers { get; private set; }

    [ObservableProperty]
    public partial bool IsSubscribed { get; private set; }

    private string _mainImagePath;

    public DetailViewModel() => WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);

    public async Task InitializeAsync(ContentSource source, string packageIdentifier)
    {
        Source = source;
        PackageIdentifier = packageIdentifier;

        HeaderText = $"{source.GetFriendlyName()} 정보";

        UpdateSubscriptionStatus();

        if (source == ContentSource.Dccon)
        {
            var packageIndex = int.Parse(packageIdentifier);
            var detail = await App.DcconClient.GetPackageDetailAsync(packageIndex);

            _mainImagePath = detail.MainImagePath;

            Title = detail.Title;
            Description = detail.Description;
            SellerName = detail.SellerName;
            Tags = detail.Tags;
            IconCount = detail.IconCount == 0 ? $"{detail.Stickers.Count}개 스티커" : $"{detail.IconCount}개 스티커";
            SaleCount = detail.SaleCount == 0 ? "판매량 정보 없음" : $"{detail.SaleCount}회 판매됨";

            var registerationDateText = DateTime.Parse(detail.RegistrationDate);
            RegisterationDate = registerationDateText.ToString("yyyy년 MM월 dd일 등록");

            if (IsSubscribed) await DownloadMainImageSourceAsync();
            else
            {
                var packages = ContentsManager.GetDownloadedPackages(source);
                var package = packages.FirstOrDefault(x => x.PackageIdentifier == packageIdentifier);
                if (package == null) await DownloadMainImageSourceAsync();
                else MainImageSource = new BitmapImage(new Uri(ContentsManager.GetMainImagePath(source, packageIdentifier, package.MainImageFileName))) { AutoPlay = SettingsManager.GifPlaybackEnabled };
            }

            Stickers = [.. detail.Stickers.Select(sticker => new StickerViewModel(packageIdentifier, sticker))];

            await Parallel.ForEachAsync(Stickers, async (sticker, _) => await sticker.FetchImageAsync());
        }
    }

    private async Task DownloadMainImageSourceAsync() => MainImageSource = await Utils.GenerateImageSourceAsync(ManageWindow.Instance.DispatcherQueue, Source, Utils.GetImageUrl(Source, _mainImagePath));

    private void UpdateSubscriptionStatus() => IsSubscribed = ContentsManager.IsPackageDownloaded(Source, PackageIdentifier);

    [RelayCommand]
    public async Task Subscribe()
    {
        if(Source == ContentSource.Dccon)
        {
            ManageWindow.ShowLoading("다운로드중...");
            try
            {
                await ContentsManager.DownloadDcconPackageAsync(int.Parse(PackageIdentifier),
                    new Progress<(int Completed, int Total)>(progress => ManageWindow.ShowLoading($"다운로드중... {progress.Completed}/{progress.Total}")));
            }
            finally { ManageWindow.HideLoading(); }
        }
    }

    [RelayCommand]
    public async Task Unsubscribe()
    {
        var result = await DialogHelper.ShowDialogAsync(ManageWindow.Instance.Content, "구독 취소", "정말로 구독을 취소하시겠습니까?", "예", "아니요");
        if (result != ContentDialogResult.Primary) return;

        ManageWindow.ShowLoading("파일 정리중...");
        try { await ContentsManager.DeletePackageAsync(Source, PackageIdentifier); }
        finally { ManageWindow.HideLoading(); }
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        if (message.Source != Source || message.Value != PackageIdentifier) return;

        UpdateSubscriptionStatus();
    }
}
