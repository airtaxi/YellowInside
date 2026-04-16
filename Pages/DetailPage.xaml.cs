using YellowInside.Models;
using YellowInside.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace YellowInside.Pages;

public sealed partial class DetailPage : Page
{
    public DetailPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        (ContentSource source, string packageIdentifier) = (ValueTuple<ContentSource, string>)e.Parameter;

        var viewModel = DataContext as DetailViewModel;
        ManageWindow.ShowLoading("상세 정보 불러오는 중...");
        try { await viewModel.InitializeAsync(source, packageIdentifier); }
        finally { ManageWindow.HideLoading(); }
        
    }
}
