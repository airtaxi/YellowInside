using YellowInside.ViewModels;
using YellowInside.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace YellowInside.Dialogs;

public sealed partial class PartialPackageExportDialog : ContentDialog
{
    public ObservableCollection<PartialPackageExportListViewModel> PackageItems { get; } = [];

    public IReadOnlyList<(ContentSource Source, int PackageIndex)> SelectedPackageKeys =>
        [.. PackageListView.SelectedItems
            .OfType<PartialPackageExportListViewModel>()
            .Select(partialPackageExportListItem => (partialPackageExportListItem.Source, partialPackageExportListItem.PackageIndex))];

    public PartialPackageExportDialog(IReadOnlyList<StickerPackage> stickerPackages)
    {
        InitializeComponent();
        RequestedTheme = SettingsManager.GetElementTheme();

        foreach (var stickerPackage in stickerPackages) PackageItems.Add(new PartialPackageExportListViewModel(stickerPackage));

        IsPrimaryButtonEnabled = false;
    }

    private void OnPackageListViewSelectionChanged(object sender, SelectionChangedEventArgs e) => IsPrimaryButtonEnabled = PackageListView.SelectedItems.Count > 0;
}
