using YellowInside.Pages.Manage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace YellowInside.Pages;

public sealed partial class ManagePage : Page
{
    public ManagePage() => InitializeComponent();

    private void OnSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem == SelectorBarItemHome) ContentFrame.Navigate(typeof(HomePage));
        else if (sender.SelectedItem == SearchSelectorBarItem) ContentFrame.Navigate(typeof(SearchPage));
        else if (sender.SelectedItem == SubscriptionsSelectorBarItem) ContentFrame.Navigate(typeof(SubscriptionsPage));
        else if (sender.SelectedItem == SelectorBarItemFavorites) ContentFrame.Navigate(typeof(FavoritesPage));
    }
}
