using YellowInside.Helpers;
using YellowInside.Pages.Manage.Arcacon;
using YellowInside.Pages.Manage.Dccon;
using YellowInside.Pages.Manage.Inven;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;

namespace YellowInside.Pages.Manage;

public sealed partial class SearchPage : Page
{
    private bool _isRestoringSelection;
    private SelectorBarItem _previousSelectorBarItem;

    public SearchPage()
    {
        InitializeComponent();
        _previousSelectorBarItem = DcconSelectorBarItem;
    }

    private async void OnSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs selectorBarSelectionChangedEventArgs)
    {
        if (_isRestoringSelection) return;
        if (sender.SelectedItem is not SelectorBarItem selectedSelectorBarItem) return;

        if (selectedSelectorBarItem == ArcaconSelectorBarItem)
        {
            var isNavigationStarted = await ArcaconSessionHelper.EnsureArcaconPageEntryAsync(this, NavigateToContentPage, typeof(ArcaconSearchPage));
            if (isNavigationStarted)
            {
                _previousSelectorBarItem = ArcaconSelectorBarItem;
                return;
            }

            RestorePreviousSelection(sender);
            return;
        }

        _previousSelectorBarItem = selectedSelectorBarItem;

        if (selectedSelectorBarItem == DcconSelectorBarItem) ContentFrame.Navigate(typeof(DcconSearchPage));
        else if (selectedSelectorBarItem == InvenStickerSelectorBarItem) ContentFrame.Navigate(typeof(InvenSearchPage));
    }

    private void NavigateToContentPage(Type pageType, object pageParameter) => ContentFrame.Navigate(pageType, pageParameter);

    private void RestorePreviousSelection(SelectorBar selectorBar)
    {
        _isRestoringSelection = true;
        selectorBar.SelectedItem = _previousSelectorBarItem;
        _isRestoringSelection = false;
    }
}
