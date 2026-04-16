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
using YellowInside.Pages.Manage;
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

    [ObservableProperty]
    public partial bool IsLocalPackage { get; private set; }

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
            var downloadedPackage = ContentsManager.GetDownloadedPackage(source, packageIdentifier);

            if (downloadedPackage is not null)
            {
                await InitializeFromLocalPackageAsync(source, packageIdentifier, downloadedPackage);
            }
            else
            {
                await InitializeFromRemoteDcconAsync(packageIdentifier);
            }
        }
        else if (source == ContentSource.Local)
        {
            var package = ContentsManager.GetDownloadedPackage(ContentSource.Local, packageIdentifier);
            if (package is null) return;

            await InitializeFromLocalPackageAsync(source, packageIdentifier, package);
            IsLocalPackage = true;
        }
    }

    private async Task InitializeFromRemoteDcconAsync(string packageIdentifier)
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

        var downloadedPackage = ContentsManager.GetDownloadedPackage(Source, packageIdentifier);
        if (downloadedPackage is not null && !string.IsNullOrEmpty(downloadedPackage.MainImageFileName))
        {
            var mainImagePath = ContentsManager.GetMainImagePath(Source, packageIdentifier, downloadedPackage.MainImageFileName);
            if (File.Exists(mainImagePath))
                MainImageSource = new BitmapImage(new Uri(mainImagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled };
            else
                await DownloadMainImageSourceAsync();
        }
        else
        {
            await DownloadMainImageSourceAsync();
        }

        Stickers = [.. detail.Stickers.Select(sticker => new StickerViewModel(packageIdentifier, sticker))];

        await Parallel.ForEachAsync(Stickers, async (sticker, _) => await sticker.FetchImageAsync());
    }

    private async Task InitializeFromLocalPackageAsync(ContentSource source, string packageIdentifier, StickerPackage package)
    {
        Title = package.Title;
        Description = package.Description;
        SellerName = string.IsNullOrWhiteSpace(package.SellerName) ? "제작자 정보 없음" : package.SellerName;
        Tags = [.. package.Tags];
        IconCount = $"{package.Stickers.Count}개 스티커";
        SaleCount = source == ContentSource.Dccon ? "판매량 정보 없음" : string.Empty;

        if (!string.IsNullOrEmpty(package.RegistrationDate))
        {
            if (DateTime.TryParse(package.RegistrationDate, out var registrationDateTime))
                RegisterationDate = registrationDateTime.ToString("yyyy년 MM월 dd일 등록");
            else
                RegisterationDate = package.RegistrationDate;
        }

        if (!string.IsNullOrEmpty(package.MainImageFileName))
        {
            var mainImagePath = ContentsManager.GetMainImagePath(source, packageIdentifier, package.MainImageFileName);
            if (File.Exists(mainImagePath))
                MainImageSource = new BitmapImage(new Uri(mainImagePath)) { AutoPlay = SettingsManager.GifPlaybackEnabled };
        }

        Stickers = [.. package.Stickers.Select(sticker => new StickerViewModel(source, packageIdentifier, sticker))];

        await Parallel.ForEachAsync(Stickers, async (sticker, _) => await sticker.FetchImageAsync());
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
        var dialogTitle = IsLocalPackage ? "삭제" : "구독 취소";
        var dialogMessage = IsLocalPackage ? "정말로 이 사용자 지정콘을 삭제하시겠습니까?" : "정말로 구독을 취소하시겠습니까?";
        var result = await DialogHelper.ShowDialogAsync(ManageWindow.Instance.Content, dialogTitle, dialogMessage, "예", "아니요");
        if (result != ContentDialogResult.Primary) return;

        ManageWindow.ShowLoading("파일 정리중...");
        try { await ContentsManager.DeletePackageAsync(Source, PackageIdentifier); }
        finally { ManageWindow.HideLoading(); }

        if (IsLocalPackage) ManageWindow.GoBack();
    }

    [RelayCommand]
    private void Edit() => ManageWindow.Navigate(typeof(CustomPackageEditorPage), new CustomPackageEditorArguments(CustomPackageEditorMode.Edit, PackageIdentifier));

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        if (message.Source != Source || message.Value != PackageIdentifier) return;

        UpdateSubscriptionStatus();
    }
}
