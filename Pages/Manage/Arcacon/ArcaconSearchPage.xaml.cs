using Arcacon.NET.Models;
using YellowInside.Helpers;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YellowInside.Pages.Manage.Arcacon;

public sealed partial class ArcaconSearchPage : Page
{
    private string _currentQuery;
    private int _currentPage;
    private bool _hasMorePages = true;
    private bool _isLoading;
    private CancellationTokenSource _searchCancellationTokenSource;

    private ObservableCollection<SearchResultViewModel> SearchResultList { get; } = [];

    public ArcaconSearchPage() => InitializeComponent();

    private async Task SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource = new CancellationTokenSource();

        _currentQuery = query;
        _currentPage = 1;
        _hasMorePages = true;
        SearchResultList.Clear();
        ResultScrollViewer.Visibility = Visibility.Collapsed;
        NoResultTextBlock.Visibility = Visibility.Collapsed;

        var hasLoadedFirstPage = await LoadPageAsync();
        if (!hasLoadedFirstPage) return;

        if (SearchResultList.Count > 0)
        {
            ResultScrollViewer.Visibility = Visibility.Visible;
            await FillViewportAsync();
        }
        else NoResultTextBlock.Visibility = Visibility.Visible;
    }

    private async Task<bool> LoadPageAsync()
    {
        if (!_hasMorePages || _isLoading) return false;

        _isLoading = true;
        try
        {
            if (await ArcaconSessionHelper.EnsureArcaconSessionAsync(
                this,
                NavigateToPage,
                typeof(ArcaconSearchPage),
                cancellationToken: _searchCancellationTokenSource?.Token ?? default) is null)
                return false;

            ManageWindow.ShowLoading("검색 중...");
            try
            {
                var searchType = SearchTypeComboBox.SelectedIndex switch
                {
                    1 => ArcaconSearchType.NickName,
                    2 => ArcaconSearchType.Tags,
                    _ => ArcaconSearchType.Title,
                };
                var searchSort = SearchSortComboBox.SelectedIndex switch
                {
                    1 => ArcaconSearchSort.New,
                    _ => ArcaconSearchSort.Hot,
                };

                var searchResult = await App.ArcaconClient.SearchAsync(
                    _currentQuery,
                    searchType,
                    searchSort,
                    _currentPage,
                    _searchCancellationTokenSource?.Token ?? default);

                foreach (var package in searchResult.Packages)
                {
                    if (!SearchResultList.Any(existing => existing.PackageIdentifier == package.PackageIndex.ToString()))
                    {
                        SearchResultList.Add(new SearchResultViewModel(package, _searchCancellationTokenSource?.Token ?? default));
                    }
                }

                if (_currentPage >= searchResult.TotalPages) _hasMorePages = false;
                else _currentPage++;

                return true;
            }
            finally { ManageWindow.HideLoading(); }
        }
        finally { _isLoading = false; }
    }

    private bool IsScrolledToBottom() => ResultScrollViewer.VerticalOffset + ResultScrollViewer.ViewportHeight >= ResultScrollViewer.ExtentHeight - 100;

    private async Task FillViewportAsync()
    {
        await Task.Delay(100);
        while (_hasMorePages && !_isLoading && IsScrolledToBottom())
        {
            var hasLoadedPage = await LoadPageAsync();
            if (!hasLoadedPage) return;

            await Task.Delay(100);
        }
    }

    private async void OnSearchAutoSuggestBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs autoSuggestBoxQuerySubmittedEventArgs)
    {
        try { await SearchAsync(autoSuggestBoxQuerySubmittedEventArgs.QueryText); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 검색할 수 없습니다.");
        }
    }

    private async void OnResultScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs scrollViewerViewChangedEventArgs)
    {
        if (scrollViewerViewChangedEventArgs.IsIntermediate) return;
        if (!IsScrolledToBottom()) return;

        try { await FillViewportAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { ManageWindow.HideLoading(); }
    }

    private async void OnResultScrollViewerSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
    {
        try { await FillViewportAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { ManageWindow.HideLoading(); }
    }

    private void NavigateToPage(Type pageType, object pageParameter)
    {
        if (Frame is not null) Frame.Navigate(pageType, pageParameter);
        else ManageWindow.Navigate(pageType, pageParameter);
    }
}
