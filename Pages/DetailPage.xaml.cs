using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using YellowInside.Helpers;
using YellowInside.Models;
using YellowInside.Pages.Manage;
using YellowInside.ViewModels;

namespace YellowInside.Pages;

public sealed partial class DetailPage : Page
{
    public DetailPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        (ContentSource source, string packageIdentifier) = (ValueTuple<ContentSource, string>)e.Parameter;
        var isDownloadedArcaconPackage = source == ContentSource.Arcacon && ContentsManager.IsPackageDownloaded(source, packageIdentifier);
        if (source == ContentSource.Arcacon && !isDownloadedArcaconPackage)
        {
            if (await ArcaconSessionHelper.EnsureArcaconSessionAsync(
                this,
                (pageType, pageParameter) => ManageWindow.Navigate(pageType, pageParameter),
                typeof(DetailPage),
                (source, packageIdentifier)) is null)
                return;
        }

        var viewModel = DataContext as DetailViewModel;
        ManageWindow.ShowLoading("상세 정보 불러오는 중...");
        try { await viewModel.InitializeAsync(source, packageIdentifier); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 이미 다운로드된 패키지만 조회할 수 있습니다.");
            ManageWindow.GoBack();
            return;
        }
        finally { ManageWindow.HideLoading(); }
        
    }
}
