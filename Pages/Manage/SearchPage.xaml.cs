using dccon.NET.Models;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YellowInside.Pages.Manage;

public sealed partial class SearchPage : Page
{
    private string _currentQuery;
    private int _currentPage;
    private bool _hasMorePages = true;
    private bool _isLoading;
    private CancellationTokenSource _searchCancellationTokenSource;

    private ObservableCollection<SearchResultViewModel> SearchResultList { get; } = [];

    public SearchPage() => InitializeComponent();

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
                1 => SearchType.NickName,
                2 => SearchType.Tags,
                _ => SearchType.Title,
            };
            var searchSort = SearchSortComboBox.SelectedIndex switch
            {
                1 => SearchSort.New,
                _ => SearchSort.Hot,
            };

            var searchResult = await App.DcconClient.SearchAsync(_currentQuery, searchType: searchType, sort: searchSort, page: _currentPage);
            foreach (var package in searchResult.Packages)
            {
                if (!SearchResultList.Any(existing => existing.PackageIdentifier == package.PackageIndex.ToString()))
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
        await Task.Delay(100); // 레이아웃 업데이트 대기
        while (_hasMorePages && !_isLoading && IsScrolledToBottom())
        {
            await LoadPageAsync();
            await Task.Delay(100);
        }
    }

    private async void OnSearchAutoSuggestBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => await SearchAsync(args.QueryText);

    private async void OnResultScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        if (!IsScrolledToBottom()) return;

        await FillViewportAsync();
    }

    private async void OnResultScrollViewerSizeChanged(object sender, SizeChangedEventArgs e) => await FillViewportAsync();
}
