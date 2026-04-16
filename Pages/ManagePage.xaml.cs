using YellowInside.Pages.Manage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YellowInside.Pages;

public sealed partial class ManagePage : Page
{
    public ManagePage() => InitializeComponent();

    public void ToggleNavigationPane() => NavigationView.IsPaneOpen = !NavigationView.IsPaneOpen;

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (NavigationView.SelectedItem is null)
            NavigationView.SelectedItem = HomeNavigationViewItem;
    }

    private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem selectedItem) return;

        var pageType = (selectedItem.Tag as string) switch
        {
            "Home" => typeof(HomePage),
            "Search" => typeof(SearchPage),
            "Subscriptions" => typeof(SubscriptionsPage),
            "Favorites" => typeof(FavoritesPage),
            "CustomPackages" => typeof(CustomPackagesPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType is not null) ContentFrame.Navigate(pageType);
    }
}
