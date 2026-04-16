using YellowInside.ViewModels;
using YellowInside.Models;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YellowInside.Dialogs;

public sealed partial class PackageSelectionDialog : ContentDialog
{
    public ObservableCollection<PackageSelectionListViewModel> PackageItems { get; } = [];

    public IReadOnlyList<(ContentSource Source, string PackageIdentifier)> SelectedPackageKeys =>
        [.. PackageListView.SelectedItems
            .OfType<PackageSelectionListViewModel>()
            .Select(item => (item.Source, item.PackageIdentifier))];

    public PackageSelectionDialog(
        string title,
        string instruction,
        string primaryButtonText,
        IEnumerable<PackageSelectionListViewModel> items)
    {
        InitializeComponent();
        RequestedTheme = SettingsManager.GetElementTheme();

        Title = title;
        PrimaryButtonText = primaryButtonText;
        InstructionTextBlock.Text = instruction;

        foreach (var item in items) PackageItems.Add(item);

        IsPrimaryButtonEnabled = false;
    }

    private void OnPackageListViewSelectionChanged(object sender, SelectionChangedEventArgs e) => IsPrimaryButtonEnabled = PackageListView.SelectedItems.Count > 0;
}
