using InvenSticker.NET.Models;
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

namespace YellowInside.Pages.Manage.Inven;

public sealed partial class InvenHomePage : Page
{
    private int _hotListPage = 1;
    private int _newListPage = 1;
    private bool _hotListHasMore = true;
    private bool _newListHasMore = true;
    private bool _isLoaded;
    private readonly SemaphoreSlim _loadMoreSemaphore = new(1, 1);
    private CancellationTokenSource _refreshCancellationTokenSource;
    private ObservableCollection<SearchResultViewModel> HotList { get; } = [];
    private ObservableCollection<SearchResultViewModel> NewList { get; } = [];

    public InvenHomePage() => InitializeComponent();

    private async Task RefreshAsync()
    {
        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _refreshCancellationTokenSource.Token;

        _hotListPage = 1;
        _newListPage = 1;
        HotList.Clear();
        NewList.Clear();

        await LoadMoreAsync(true, cancellationToken);
        await LoadMoreAsync(false, cancellationToken);

        _isLoaded = true;
    }

    private async Task LoadMoreAsync(bool isHot, CancellationToken cancellationToken = default)
    {
        await _loadMoreSemaphore.WaitAsync();
        try
        {
            IsEnabled = false;
            if (isHot) ManageWindow.ShowLoading("인기 스티커 불러오는 중...");
            else ManageWindow.ShowLoading("최신 스티커 불러오는 중...");

            try
            {
                if (isHot)
                {
                    if (!_hotListHasMore) return;

                    var result = await App.InvenStickerClient.GetListAsync(sort: InvenStickerListSort.Sales, page: _hotListPage++);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_hotListPage > result.TotalPages) _hotListHasMore = false;

                    var viewModels = result.Packages.Select(c => new SearchResultViewModel(c, cancellationToken));
                    foreach (var viewModel in viewModels.Where(x => !HotList.Any(y => x.PackageIdentifier == y.PackageIdentifier))) HotList.Add(viewModel);
                }
                else
                {
                    if (!_newListHasMore) return;

                    var result = await App.InvenStickerClient.GetListAsync(sort: InvenStickerListSort.Recent, page: _newListPage++);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_newListPage > result.TotalPages) _newListHasMore = false;

                    var viewModels = result.Packages.Select(c => new SearchResultViewModel(c, cancellationToken));
                    foreach (var viewModel in viewModels.Where(x => !NewList.Any(y => x.PackageIdentifier == y.PackageIdentifier))) NewList.Add(viewModel);
                }
            }
            finally
            {
                IsEnabled = true;
                ManageWindow.HideLoading();
            }
        }
        finally { _loadMoreSemaphore.Release(); }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!_isLoaded)
        {
            try { await RefreshAsync(); }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ManageWindow.HideLoading();
                await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 홈 목록을 불러올 수 없습니다.");
            }
        }
    }

    private async void OnRefreshButtonClicked(object sender, RoutedEventArgs e)
    {
        try { await RefreshAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 홈 목록을 불러올 수 없습니다.");
        }
    }

    private async void OnScrollViewViewChanged(ScrollView scrollView, object args)
    {
        var isHorizontalScrollReached = scrollView.HorizontalOffset + scrollView.ViewportWidth >= scrollView.ExtentWidth;
        if (!isHorizontalScrollReached) return;

        var isLoadMoreInProgress = _loadMoreSemaphore.CurrentCount == 0;
        if (isLoadMoreInProgress) return;

        scrollView.ScrollTo(scrollView.HorizontalOffset - 1, 0);

        var isHot = scrollView.Tag as string == "Hot";
        try { await LoadMoreAsync(isHot, _refreshCancellationTokenSource?.Token ?? default); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
        }
    }

    private readonly Dictionary<ScrollView, double> _targetHorizontalOffset = [];
    private void OnScrollViewPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;

        var scrollView = sender as ScrollView;
        if (scrollView == null) return;

        if (!_targetHorizontalOffset.ContainsKey(scrollView)) _targetHorizontalOffset.Add(scrollView, scrollView.HorizontalOffset);
        _targetHorizontalOffset[scrollView] = Math.Clamp(_targetHorizontalOffset[scrollView] - delta, 0, scrollView.ExtentWidth - scrollView.ViewportWidth);
        scrollView.ScrollTo(_targetHorizontalOffset[scrollView], 0);

        e.Handled = true;
    }
}
