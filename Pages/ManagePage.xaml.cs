using YellowInside.Helpers;
using YellowInside.Models;
using YellowInside.Pages.Manage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace YellowInside.Pages;

public sealed partial class ManagePage : Page
{
    private const string StoreProductId = "9PG8PCFFZ7BD";
    private static readonly Uri s_storeApiUri = new($"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{StoreProductId}?market=KR&locale=ko-KR&deviceFamily=Windows.Desktop");

    private static bool s_whatsNewChecked;

    public ManagePage() => InitializeComponent();

    public void ToggleNavigationPane() => NavigationView.IsPaneOpen = !NavigationView.IsPaneOpen;

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (NavigationView.SelectedItem is null)
            NavigationView.SelectedItem = HomeNavigationViewItem;

        await ShowWhatsNewIfFirstLaunchAsync();
    }

    private async Task ShowWhatsNewIfFirstLaunchAsync()
    {
        if (s_whatsNewChecked) return;
        s_whatsNewChecked = true;

        var version = Package.Current.Id.Version;
        var currentVersion = $"{version.Major}.{version.Minor}.{version.Build}";
        if (SettingsManager.LastSeenVersion == currentVersion) return;

        SettingsManager.LastSeenVersion = currentVersion;

        try
        {
            var whatsNew = await FetchWhatsNewAsync();
            if (string.IsNullOrWhiteSpace(whatsNew)) return;

            var textBlock = new TextBlock
            {
                Text = whatsNew,
                TextWrapping = TextWrapping.Wrap,
            };
            var scrollViewer = new ScrollViewer
            {
                Content = textBlock,
                MaxHeight = 400,
            };

            var dialog = this.GenerateMessageDialog($"v{currentVersion} 업데이트 노트", null);
            dialog.Content = scrollViewer;
            await dialog.ShowAsync();
        }
        catch
        {
            // Silently ignore network or parsing errors
        }
    }

    private static async Task<string> FetchWhatsNewAsync()
    {
        using var httpClient = new HttpClient();
        var json = await httpClient.GetStringAsync(s_storeApiUri);
        var response = JsonSerializer.Deserialize(json, StoreProductJsonContext.Default.StoreProductResponse);
        return response?.Payload?.Notes?.FirstOrDefault();
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
