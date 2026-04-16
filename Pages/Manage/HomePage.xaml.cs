using YellowInside.Pages.Manage.Arcacon;
using YellowInside.Pages.Manage.Dccon;
using YellowInside.Pages.Manage.Inven;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace YellowInside.Pages.Manage;

public sealed partial class HomePage : Page
{
    public HomePage() => InitializeComponent();

    private void OnSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem == DcconSelectorBarItem) ContentFrame.Navigate(typeof(DcconHomePage));
        else if (sender.SelectedItem == ArcaconSelectorBarItem) ContentFrame.Navigate(typeof(ArcaconHomePage));
        else if (sender.SelectedItem == InvenStickerSelectorBarItem) ContentFrame.Navigate(typeof(InvenHomePage));
    }
}
