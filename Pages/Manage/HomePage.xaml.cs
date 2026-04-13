using dccon.NET.Models;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YellowInside.Pages.Manage;

public sealed partial class HomePage : Page
{
    private int _hotListPage = 1;
    private int _newListPage = 1;
    private bool _hotListHasMore = true;
    private bool _newListHasMore = true;
    private bool _isLoaded;
    private CancellationTokenSource _refreshCancellationTokenSource;
    private ObservableCollection<SearchResultViewModel> DailyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> WeeklyPopularList { get; } = [];
    private ObservableCollection<SearchResultViewModel> HotList { get; } = [];
    private ObservableCollection<SearchResultViewModel> NewList { get; } = [];

    public HomePage() => InitializeComponent();

    private async Task RefreshAsync()
    {
        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _refreshCancellationTokenSource.Token;

        DailyPopularList.Clear();
        WeeklyPopularList.Clear();

        ManageWindow.ShowLoading("일간 인기 디시콘 불러오는 중...");
        try
        {
            var dailyPopularList = await App.DcconClient.GetDailyPopularAsync();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var dailyPopular in dailyPopularList) DailyPopularList.Add(new SearchResultViewModel(dailyPopular, cancellationToken));
        }
        finally { ManageWindow.HideLoading(); }

        ManageWindow.ShowLoading("주간 인기 디시콘 불러오는 중...");
        try
        {
            var weeklyPopularList = await App.DcconClient.GetWeeklyPopularAsync();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var weeklyPopular in weeklyPopularList) WeeklyPopularList.Add(new SearchResultViewModel(weeklyPopular, cancellationToken));
        }
        finally { ManageWindow.HideLoading(); }

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
        IsEnabled = false;
        if (isHot) ManageWindow.ShowLoading("인기콘 불러오는 중...");
        else ManageWindow.ShowLoading("최신콘 불러오는 중...");

        try
        {
            if (isHot)
            {
                if (!_hotListHasMore) return;

                var searchResult = await App.DcconClient.GetHotListAsync(_hotListPage++);
                cancellationToken.ThrowIfCancellationRequested();
                if (_hotListPage >= searchResult.TotalPages) _hotListHasMore = false;

                var viewModels = searchResult.Packages.Select(c => new SearchResultViewModel(c, cancellationToken));
                foreach (var vm in viewModels.Where(x => !HotList.Any(y => x.PackageIndex == y.PackageIndex))) HotList.Add(vm);
            }
            else
            {
                if (!_newListHasMore) return;

                var searchResult = await App.DcconClient.GetNewListAsync(_newListPage++);
                cancellationToken.ThrowIfCancellationRequested();
                if (_newListPage >= searchResult.TotalPages) _newListHasMore = false;

                var viewModels = searchResult.Packages.Select(c => new SearchResultViewModel(c, cancellationToken));
                foreach (var vm in viewModels.Where(x => !NewList.Any(y => x.PackageIndex == y.PackageIndex))) NewList.Add(vm);
            }
        }
        finally
        {
            IsEnabled = true;
            ManageWindow.HideLoading();
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!_isLoaded) await RefreshAsync();
    }

    private async void OnRefreshButtonClicked(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnScrollViewViewChanged(ScrollView scrollView, object args)
    {
        var isHorizontalScrollReached = scrollView.HorizontalOffset + scrollView.ViewportWidth >= scrollView.ExtentWidth - 100;
        if (!isHorizontalScrollReached) return;

        scrollView.ScrollTo(scrollView.HorizontalOffset, 0); // 스크롤 위치 고정 (ViewChanged 이벤트가 여러 번 발생하는 문제 방지)

        var isHot = scrollView.Tag as string == "Hot";
        await LoadMoreAsync(isHot, _refreshCancellationTokenSource?.Token ?? default);
    }

    private void OnScrollViewPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;

        var scrollView = sender as ScrollView;
        scrollView?.ScrollBy(-delta, 0);

        e.Handled = true;
    }
}
