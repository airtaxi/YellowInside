using Arcacon.NET.Models;
using YellowInside.Helpers;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YellowInside.Pages.Manage.Arcacon;

public sealed partial class ArcaconHomePage : Page
{
    private int _hotListPageNumber = 1;
    private int _newListPageNumber = 1;
    private bool _hasMoreHotListPages = true;
    private bool _hasMoreNewListPages = true;
    private bool _isLoaded;
    private readonly SemaphoreSlim _loadMoreSemaphore = new(1, 1);
    private CancellationTokenSource _refreshCancellationTokenSource;
    private readonly Dictionary<ScrollView, double> _targetHorizontalOffsetsByScrollView = [];

    private ObservableCollection<SearchResultViewModel> DailyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> WeeklyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> MonthlyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> HotList { get; } = [];
    private ObservableCollection<SearchResultViewModel> NewList { get; } = [];

    public ArcaconHomePage() => InitializeComponent();

    private async Task RefreshAsync()
    {
        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();

        var cancellationToken = _refreshCancellationTokenSource.Token;

        _hotListPageNumber = 1;
        _newListPageNumber = 1;
        _hasMoreHotListPages = true;
        _hasMoreNewListPages = true;

        DailyPopularList.Clear();
        WeeklyPopularList.Clear();
        MonthlyPopularList.Clear();
        HotList.Clear();
        NewList.Clear();

        var latestSearchResult = await ArcaconSessionHelper.EnsureArcaconSessionAsync(
            this,
            NavigateToPage,
            typeof(ArcaconHomePage),
            cancellationToken: cancellationToken);
        if (latestSearchResult is null) return;

        cancellationToken.ThrowIfCancellationRequested();

        AddPackagesToTargetList(NewList, latestSearchResult.Packages.Select(package => new SearchResultViewModel(package, cancellationToken)));

        if (latestSearchResult.TotalPages <= 1) _hasMoreNewListPages = false;
        else _newListPageNumber = 2;

        await LoadPopularPackagesAsync(
            "일간 인기 아카콘 불러오는 중...",
            (cancellationToken) => App.ArcaconClient.GetDailyPopularAsync(cancellationToken),
            DailyPopularList,
            cancellationToken);
        await LoadPopularPackagesAsync(
            "주간 인기 아카콘 불러오는 중...",
            (cancellationToken) => App.ArcaconClient.GetWeeklyPopularAsync(cancellationToken),
            WeeklyPopularList,
            cancellationToken);
        await LoadPopularPackagesAsync(
            "월간 인기 아카콘 불러오는 중...",
            (cancellationToken) => App.ArcaconClient.GetMonthlyPopularAsync(cancellationToken),
            MonthlyPopularList,
            cancellationToken);
        await LoadMoreAsync(isHotList: true, cancellationToken);

        _isLoaded = true;
    }

    private static void AddPackagesToTargetList(ObservableCollection<SearchResultViewModel> targetList, IEnumerable<SearchResultViewModel> viewModels)
    {
        foreach (var viewModel in viewModels.Where(viewModel => !targetList.Any(existing => existing.PackageIdentifier == viewModel.PackageIdentifier)))
        {
            targetList.Add(viewModel);
        }
    }

    private static async Task LoadPopularPackagesAsync(
        string loadingMessage,
        Func<CancellationToken, Task<IReadOnlyList<ArcaconPackageSummary>>> getPopularPackagesAsync,
        ObservableCollection<SearchResultViewModel> targetList,
        CancellationToken cancellationToken)
    {
        ManageWindow.ShowLoading(loadingMessage);
        try
        {
            var popularPackages = await getPopularPackagesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            AddPackagesToTargetList(targetList, popularPackages.Select(package => new SearchResultViewModel(package, cancellationToken)));
        }
        finally { ManageWindow.HideLoading(); }
    }

    private async Task LoadMoreAsync(bool isHotList, CancellationToken cancellationToken = default)
    {
        await _loadMoreSemaphore.WaitAsync(cancellationToken);
        try
        {
            ManageWindow.ShowLoading(isHotList ? "인기 아카콘 불러오는 중..." : "최신 아카콘 불러오는 중...");
            try
            {
                if (isHotList)
                {
                    if (!_hasMoreHotListPages) return;
                    if (await ArcaconSessionHelper.EnsureArcaconSessionAsync(this, NavigateToPage, typeof(ArcaconHomePage), cancellationToken: cancellationToken) is null) return;
                }
                else
                {
                    if (!_hasMoreNewListPages) return;
                    if (await ArcaconSessionHelper.EnsureArcaconSessionAsync(this, NavigateToPage, typeof(ArcaconHomePage), cancellationToken: cancellationToken) is null) return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (isHotList)
                {
                    var hotSearchResult = await App.ArcaconClient.GetHotListAsync(_hotListPageNumber++, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_hotListPageNumber > hotSearchResult.TotalPages) _hasMoreHotListPages = false;

                    AddPackagesToTargetList(HotList, hotSearchResult.Packages.Select(package => new SearchResultViewModel(package, cancellationToken)));
                }
                else
                {
                    var newSearchResult = await App.ArcaconClient.GetNewListAsync(_newListPageNumber++, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_newListPageNumber > newSearchResult.TotalPages) _hasMoreNewListPages = false;

                    AddPackagesToTargetList(NewList, newSearchResult.Packages.Select(package => new SearchResultViewModel(package, cancellationToken)));
                }
            }
            finally { ManageWindow.HideLoading(); }
        }
        finally { _loadMoreSemaphore.Release(); }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs navigationEventArgs)
    {
        base.OnNavigatedTo(navigationEventArgs);

        if (_isLoaded) return;

        try { await RefreshAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 홈 목록을 불러올 수 없습니다.");
        }
    }

    private async void OnRefreshButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        try { await RefreshAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 홈 목록을 불러올 수 없습니다.");
        }
    }

    private async void OnScrollViewViewChanged(ScrollView scrollView, object eventArguments)
    {
        var hasReachedHorizontalScrollEnd = scrollView.HorizontalOffset + scrollView.ViewportWidth >= scrollView.ExtentWidth;
        if (!hasReachedHorizontalScrollEnd) return;

        var isLoadMoreInProgress = _loadMoreSemaphore.CurrentCount == 0;
        if (isLoadMoreInProgress) return;

        scrollView.ScrollTo(scrollView.HorizontalOffset - 1, 0);

        var isHotList = scrollView.Tag as string == "Hot";
        try { await LoadMoreAsync(isHotList, _refreshCancellationTokenSource?.Token ?? default); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { ManageWindow.HideLoading(); }
    }

    private void OnScrollViewPointerWheelChanged(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        var mouseWheelDelta = pointerRoutedEventArgs.GetCurrentPoint(null).Properties.MouseWheelDelta;

        if (sender is not ScrollView scrollView) return;

        if (!_targetHorizontalOffsetsByScrollView.ContainsKey(scrollView)) _targetHorizontalOffsetsByScrollView.Add(scrollView, scrollView.HorizontalOffset);

        _targetHorizontalOffsetsByScrollView[scrollView] = Math.Clamp(_targetHorizontalOffsetsByScrollView[scrollView] - mouseWheelDelta, 0, scrollView.ExtentWidth - scrollView.ViewportWidth);
        scrollView.ScrollTo(_targetHorizontalOffsetsByScrollView[scrollView], 0);

        pointerRoutedEventArgs.Handled = true;
    }

    private void NavigateToPage(Type pageType, object pageParameter)
    {
        if (Frame is not null) Frame.Navigate(pageType, pageParameter);
        else ManageWindow.Navigate(pageType, pageParameter);
    }
}
