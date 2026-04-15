using YellowInside.Models;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YellowInside.Dialogs;

public sealed partial class ReplaceImportWarningDialog : ContentDialog
{
    public ObservableCollection<ReplaceImportWarningListViewModel> DeletedPackageItems { get; } = [];

    public string AdditionalDeletedPackagesText { get; }

    public Visibility AdditionalDeletedPackagesVisibility { get; }

    public ReplaceImportWarningDialog(IReadOnlyList<StickerPackage> deletedPackages)
    {
        InitializeComponent();
        RequestedTheme = SettingsManager.GetElementTheme();

        var previewPackages = deletedPackages.Take(6).ToList();
        foreach (var deletedPackage in previewPackages) DeletedPackageItems.Add(new ReplaceImportWarningListViewModel(deletedPackage));

        if (deletedPackages.Count > previewPackages.Count)
        {
            AdditionalDeletedPackagesText = $"외 {deletedPackages.Count - previewPackages.Count}개 패키지가 더 삭제됩니다.";
            AdditionalDeletedPackagesVisibility = Visibility.Visible;
        }
        else
        {
            AdditionalDeletedPackagesText = string.Empty;
            AdditionalDeletedPackagesVisibility = Visibility.Collapsed;
        }
    }
}
