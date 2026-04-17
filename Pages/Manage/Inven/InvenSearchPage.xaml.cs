using InvenSticker.NET.Models;
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

namespace YellowInside.Pages.Manage.Inven;

public sealed partial class InvenSearchPage : Page
{
    private string _currentQuery;
    private int _currentPage;
    private bool _hasMorePages = true;
    private bool _isLoading;
    private CancellationTokenSource _searchCancellationTokenSource;

    private ObservableCollection<SearchResultViewModel> SearchResultList { get; } = [];

    public InvenSearchPage() => InitializeComponent();

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

        await LoadPageAsync();

        if (SearchResultList.Count > 0)
        {
            ResultScrollViewer.Visibility = Visibility.Visible;
            await FillViewportAsync();
        }
        else NoResultTextBlock.Visibility = Visibility.Visible;
    }

    private async Task LoadPageAsync()
    {
        if (!_hasMorePages || _isLoading) return;

        _isLoading = true;
        ManageWindow.ShowLoading("검색 중...");
        try
        {
            var searchType = SearchTypeComboBox.SelectedIndex switch
            {
                1 => InvenStickerSearchType.NickName,
                2 => InvenStickerSearchType.Tag,
                _ => InvenStickerSearchType.Name,
            };
            var searchSort = SearchSortComboBox.SelectedIndex switch
            {
                1 => InvenStickerListSort.Recent,
                _ => InvenStickerListSort.Sales,
            };

            var searchResult = await App.InvenStickerClient.SearchAsync(_currentQuery, searchType: searchType, sort: searchSort, page: _currentPage);
            foreach (var package in searchResult.Packages)
            {
                if (!SearchResultList.Any(existing => existing.PackageIdentifier == package.PackageId.ToString()))
                    SearchResultList.Add(new SearchResultViewModel(package, _searchCancellationTokenSource?.Token ?? default));
            }

            if (_currentPage >= searchResult.TotalPages) _hasMorePages = false;
            else _currentPage++;
        }
        finally
        {
            _isLoading = false;
            ManageWindow.HideLoading();
        }
    }

    private bool IsScrolledToBottom()
        => ResultScrollViewer.VerticalOffset + ResultScrollViewer.ViewportHeight >= ResultScrollViewer.ExtentHeight - 100;

    private async Task FillViewportAsync()
    {
        await Task.Delay(100);
        while (_hasMorePages && !_isLoading && IsScrolledToBottom())
        {
            await LoadPageAsync();
            await Task.Delay(100);
        }
    }

    private async void OnSearchAutoSuggestBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try { await SearchAsync(args.QueryText); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
            await this.ShowDialogAsync("네트워크 오류", "인터넷 연결을 확인해 주세요.\n오프라인 상태에서는 검색할 수 없습니다.");
        }
    }

    private async void OnResultScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        if (!IsScrolledToBottom()) return;

        try { await FillViewportAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
        }
    }

    private async void OnResultScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        try { await FillViewportAsync(); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ManageWindow.HideLoading();
        }
    }
}
