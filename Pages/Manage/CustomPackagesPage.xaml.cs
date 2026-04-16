using CommunityToolkit.Mvvm.Messaging;
using YellowInside.Messages;
using YellowInside.Models;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Linq;

namespace YellowInside.Pages.Manage;

public sealed partial class CustomPackagesPage : Page
{
    private ObservableCollection<SubscriptionViewModel> CustomPackageList { get; } = [];

    public CustomPackagesPage()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<FavoritesOrPackagesChangedMessage>(this, OnFavoritesOrPackagesChangedMessageReceived);
    }

    private bool _isLoaded;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_isLoaded) return;

        RefreshList();
        _isLoaded = true;
    }

    private void RefreshList()
    {
        CustomPackageList.Clear();
        var packages = ContentsManager.GetDownloadedPackages(ContentSource.Local);
        foreach (var package in packages) CustomPackageList.Add(new SubscriptionViewModel(package));

        NoCustomPackagesTextBlock.Visibility = CustomPackageList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFavoritesOrPackagesChangedMessageReceived(object recipient, FavoritesOrPackagesChangedMessage message)
    {
        if (message.Source != ContentSource.Local) return;

        var packages = ContentsManager.GetDownloadedPackages(ContentSource.Local);

        var viewModelsToAdd = packages
            .Where(package => !CustomPackageList.Any(viewModel => viewModel.PackageIdentifier == package.PackageIdentifier))
            .Select(package => new SubscriptionViewModel(package))
            .ToList();
        foreach (var viewModel in viewModelsToAdd) CustomPackageList.Add(viewModel);

        var viewModelsToRemove = CustomPackageList
            .Where(viewModel => !packages.Any(package => package.PackageIdentifier == viewModel.PackageIdentifier))
            .ToList();
        foreach (var viewModel in viewModelsToRemove) CustomPackageList.Remove(viewModel);

        NoCustomPackagesTextBlock.Visibility = CustomPackageList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddCustomPackageButtonClicked(object sender, RoutedEventArgs e)
        => ManageWindow.Navigate(typeof(CustomPackageEditorPage), new CustomPackageEditorArguments(CustomPackageEditorMode.Add));
}
