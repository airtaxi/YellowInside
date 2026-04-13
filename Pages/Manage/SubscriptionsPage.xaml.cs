using CommunityToolkit.Mvvm.Messaging;
using YellowInside.Messages;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Linq;

namespace YellowInside.Pages.Manage;

public sealed partial class SubscriptionsPage : Page
{
    private ObservableCollection<SubscriptionViewModel> SubscriptionList { get; } = [];

    public SubscriptionsPage()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    private bool _isLoaded;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_isLoaded) return;

        SubscriptionList.Clear();
        var subscriptions = ContentsManager.GetDownloadedPackages();
        foreach (var subscription in subscriptions) SubscriptionList.Add(new SubscriptionViewModel(subscription));

        NoSubscriptionsTextBlock.Visibility = SubscriptionList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _isLoaded = true;
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        var subscriptions = ContentsManager.GetDownloadedPackages();

        var viewModelsToAdd = subscriptions.Where(s => !SubscriptionList.Any(vm => vm.PackageIndex == s.PackageIndex)).Select(s => new SubscriptionViewModel(s)).ToList();
        foreach (var viewModel in viewModelsToAdd) SubscriptionList.Add(viewModel);

        var viewModelsToRemove = SubscriptionList.Where(vm => !subscriptions.Any(s => s.PackageIndex == vm.PackageIndex)).ToList();
        foreach (var viewModel in viewModelsToRemove) SubscriptionList.Remove(viewModel);

        NoSubscriptionsTextBlock.Visibility = SubscriptionList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnReorderToggleSwitchToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ReorderToggleSwitch.IsOn) return;

        var subscriptions = ContentsManager.GetDownloadedPackages();
        SubscriptionList.Clear();
        foreach (var subscription in subscriptions) SubscriptionList.Add(new SubscriptionViewModel(subscription));
    }

    private async void OnReorderListViewDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs arguments)
    {
        var orderedPackageKeys = SubscriptionList
            .Select(viewModel => (viewModel.Source, viewModel.PackageIndex))
            .ToList();
        await ContentsManager.ReorderPackagesAsync(orderedPackageKeys);
    }
}
