using YellowInside.Helpers;
using YellowInside.Models;
using YellowInside.Pages.Manage.Arcacon;
using YellowInside.Pages.Manage.Dccon;
using YellowInside.Pages.Manage.Inven;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.Storage;

namespace YellowInside.Pages.Manage;

public sealed partial class HomePage : Page
{
    private const string SettingsKeyContentSourceSupportTipDismissed = "ManageContentSourceSupportTipDismissed";

    private bool _isRestoringSelection;
    private SelectorBarItem _previousSelectorBarItem;

    public HomePage()
    {
        InitializeComponent();
        _previousSelectorBarItem = DcconSelectorBarItem;
        ContentSourceSupportTeachingTip.Target = ContentSourceSelectorBar;
        Loaded += OnPageLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs navigationEventArgs)
    {
        base.OnNavigatedTo(navigationEventArgs);

        if (navigationEventArgs.Parameter is HomePageNavigationArguments { OpenArcaconPage: true })
            NavigateToArcaconContentPage();
    }

    private async void OnSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArgs)
    {
        if (_isRestoringSelection) return;
        if (sender.SelectedItem is not SelectorBarItem selectedSelectorBarItem) return;

        if (selectedSelectorBarItem == ArcaconSelectorBarItem)
        {
            var isNavigationStarted = await ArcaconSessionHelper.EnsureArcaconPageEntryAsync(this, NavigateToContentPage, typeof(ArcaconHomePage));
            if (isNavigationStarted)
            {
                _previousSelectorBarItem = ArcaconSelectorBarItem;
                return;
            }

            RestorePreviousSelection(sender);
            return;
        }

        _previousSelectorBarItem = selectedSelectorBarItem;

        if (selectedSelectorBarItem == DcconSelectorBarItem) ContentFrame.Navigate(typeof(DcconHomePage));
        else if (selectedSelectorBarItem == InvenStickerSelectorBarItem) ContentFrame.Navigate(typeof(InvenHomePage));
    }

    private void NavigateToContentPage(Type pageType, object pageParameter) => ContentFrame.Navigate(pageType, pageParameter);

    private void NavigateToArcaconContentPage()
    {
        _isRestoringSelection = true;
        ContentSourceSelectorBar.SelectedItem = ArcaconSelectorBarItem;
        _isRestoringSelection = false;
        _previousSelectorBarItem = ArcaconSelectorBarItem;
        ContentFrame.Navigate(typeof(ArcaconHomePage));
    }

    private void RestorePreviousSelection(SelectorBar selectorBar)
    {
        _isRestoringSelection = true;
        selectorBar.SelectedItem = _previousSelectorBarItem;
        _isRestoringSelection = false;
    }

    private void OnPageLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs routedEventArgs)
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        if (localSettings.Values.ContainsKey(SettingsKeyContentSourceSupportTipDismissed)) return;
        ContentSourceSupportTeachingTip.IsOpen = true;
    }

    private void OnContentSourceSupportTeachingTipActionButtonClicked(TeachingTip sender, object args)
    {
        ApplicationData.Current.LocalSettings.Values[SettingsKeyContentSourceSupportTipDismissed] = true;
        ContentSourceSupportTeachingTip.IsOpen = false;
    }
}
