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
using YellowInside.Helpers;
using YellowInside.ViewModels;

namespace YellowInside.Pages.Manage.Dccon;

public sealed partial class DcconHomePage : Page
{
    private int _newListPageNumber = 1;
    private bool _hasMoreNewListPages = true;
    private bool _isLoaded;
    private readonly SemaphoreSlim _loadMoreSemaphore = new(1, 1);
    private CancellationTokenSource _refreshCancellationTokenSource;
    private readonly Dictionary<ScrollView, double> _targetHorizontalOffsetsByScrollView = [];

    private ObservableCollection<SearchResultViewModel> DailyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> WeeklyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> MonthlyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> NewList { get; } = [];

    public DcconHomePage() => InitializeComponent();

    private async Task RefreshAsync()
    {
        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _refreshCancellationTokenSource.Token;

        _newListPageNumber = 1;
        _hasMoreNewListPages = true;

        DailyPopularList.Clear();
        WeeklyPopularList.Clear();
        MonthlyPopularList.Clear();
        NewList.Clear();

        await LoadPopularPackagesAsync(
            "일간 인기 디시콘 불러오는 중...",
            async () => (await App.DcconClient.GetDailyPopularAsync()).Select(package => new SearchResultViewModel(package, cancellationToken)),
            DailyPopularList,
            cancellationToken);
        await LoadPopularPackagesAsync(
            "주간 인기 디시콘 불러오는 중...",
            async () => (await App.DcconClient.GetWeeklyPopularAsync()).Select(package => new SearchResultViewModel(package, cancellationToken)),
            WeeklyPopularList,
            cancellationToken);
        await LoadPopularPackagesAsync(
            "월간 인기 디시콘 불러오는 중...",
            async () => (await App.DcconClient.GetMonthlyPopularAsync()).Select(package => new SearchResultViewModel(package, cancellationToken)),
            MonthlyPopularList,
            cancellationToken);

        await LoadMoreNewListAsync(cancellationToken);

        _isLoaded = true;
    }

    private static void AddPackagesToTargetList(ObservableCollection<SearchResultViewModel> targetList, IEnumerable<SearchResultViewModel> viewModels)
    {
        foreach (var viewModel in viewModels.Where(viewModel => !targetList.Any(existingViewModel => existingViewModel.PackageIdentifier == viewModel.PackageIdentifier)))
        {
            targetList.Add(viewModel);
        }
    }

    private static async Task LoadPopularPackagesAsync(
        string loadingMessage,
        Func<Task<IEnumerable<SearchResultViewModel>>> getPopularPackageViewModelsAsync,
        ObservableCollection<SearchResultViewModel> targetList,
        CancellationToken cancellationToken)
    {
        ManageWindow.ShowLoading(loadingMessage);
        try
        {
            var viewModels = await getPopularPackageViewModelsAsync();
            cancellationToken.ThrowIfCancellationRequested();

            AddPackagesToTargetList(targetList, viewModels);
        }
        finally { ManageWindow.HideLoading(); }
    }

    private async Task LoadMoreNewListAsync(CancellationToken cancellationToken = default)
    {
        await _loadMoreSemaphore.WaitAsync(cancellationToken);
        try
        {
            ManageWindow.ShowLoading("최신 디시콘 불러오는 중...");
            try
            {
                if (!_hasMoreNewListPages) return;

                var searchResult = await App.DcconClient.GetNewListAsync(_newListPageNumber++, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (_newListPageNumber > searchResult.TotalPages) _hasMoreNewListPages = false;

                AddPackagesToTargetList(NewList, searchResult.Packages.Select(package => new SearchResultViewModel(package, cancellationToken)));
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

        try { await LoadMoreNewListAsync(_refreshCancellationTokenSource?.Token ?? default); }
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
}
