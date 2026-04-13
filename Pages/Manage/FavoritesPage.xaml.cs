using CommunityToolkit.Mvvm.Messaging;
using YellowInside.Messages;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Linq;

namespace YellowInside.Pages.Manage;

public sealed partial class FavoritesPage : Page
{
    private ObservableCollection<FavoriteViewModel> FavoriteList { get; } = [];

    public FavoritesPage()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    private bool _isLoaded;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_isLoaded) return;

        FavoriteList.Clear();
        var subscriptions = ContentsManager.GetDownloadedPackages();
        var favorites = ContentsManager.GetFavorites();
        var subscriptionsWithFavorites = subscriptions.Where(s => favorites.Any(f => f.Source == s.Source && f.PackageIndex == s.PackageIndex)).ToList();

        foreach (var subscription in subscriptionsWithFavorites) FavoriteList.Add(new FavoriteViewModel(subscription));

        NoFavoritesTextBlock.Visibility = FavoriteList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _isLoaded = true;
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        var subscriptions = ContentsManager.GetDownloadedPackages();
        var favorites = ContentsManager.GetFavorites();
        var subscriptionsWithFavorites = subscriptions.Where(s => favorites.Any(f => f.Source == s.Source && f.PackageIndex == s.PackageIndex)).ToList();

        var viewModelsToAdd = subscriptionsWithFavorites.Where(s => !FavoriteList.Any(vm => vm.Source == s.Source && vm.PackageIndex == s.PackageIndex)).Select(s => new FavoriteViewModel(s)).ToList();
        foreach (var viewModel in viewModelsToAdd) FavoriteList.Add(viewModel);

        var viewModelsToRemove = FavoriteList.Where(vm => !subscriptionsWithFavorites.Any(s => s.Source == vm.Source && s.PackageIndex == vm.PackageIndex)).ToList();
        foreach (var viewModel in viewModelsToRemove) FavoriteList.Remove(viewModel);

        NoFavoritesTextBlock.Visibility = FavoriteList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
