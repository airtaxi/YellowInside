using YellowInside.Pages.Manage.Arcacon;
using YellowInside.Pages.Manage.Dccon;
using YellowInside.Pages.Manage.Inven;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace YellowInside.Pages.Manage;

public sealed partial class SearchPage : Page
{
    public SearchPage() => InitializeComponent();

    private void OnSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem == DcconSelectorBarItem) ContentFrame.Navigate(typeof(DcconSearchPage));
        else if (sender.SelectedItem == ArcaconSelectorBarItem) ContentFrame.Navigate(typeof(ArcaconSearchPage));
        else if (sender.SelectedItem == InvenStickerSelectorBarItem) ContentFrame.Navigate(typeof(InvenSearchPage));
    }
}
