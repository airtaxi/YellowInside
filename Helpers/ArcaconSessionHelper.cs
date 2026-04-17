using Arcacon.NET.Exceptions;
using Arcacon.NET.Models;
using YellowInside.Pages.Manage.Arcacon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using YellowInside.Models;

namespace YellowInside.Helpers;

public static class ArcaconSessionHelper
{
    private const string EntryDialogTitle = "아카콘 로그인";
    private const string EntryDialogDescription = "아카콘을 사용하려면 아카라이브 로그인이 필요합니다.\n로그인 페이지로 이동할까요?";
    private const string SessionExpiredDialogDescription = "로그인이 필요하거나 세션이 만료되었습니다.\n로그인 페이지로 이동할까요?";

    public static async Task<bool> EnsureArcaconPageEntryAsync(
        UIElement dialogHostElement,
        Action<Type, object> navigateAction,
        Type targetPageType,
        object targetPageParameter = null)
    {
        if (App.ArcaconClient.IsLoggedIn)
        {
            navigateAction(targetPageType, targetPageParameter);
            return true;
        }

        var contentDialogResult = await dialogHostElement.ShowDialogAsync(
            EntryDialogTitle,
            EntryDialogDescription,
            "이동",
            "취소");
        if (contentDialogResult != ContentDialogResult.Primary) return false;

        NavigateToArcaconLoginPage(navigateAction, targetPageType, targetPageParameter);
        return true;
    }

    public static async Task<ArcaconSearchResult> EnsureArcaconSessionAsync(
        UIElement dialogHostElement,
        Action<Type, object> navigateAction,
        Type returnPageType,
        object returnPageParameter = null,
        CancellationToken cancellationToken = default)
    {
        try { return await App.ArcaconClient.GetNewListAsync(cancellationToken: cancellationToken); }
        catch (Exception exception) when (exception is ArcaconLoginException or InvalidOperationException)
        {
            var contentDialogResult = await dialogHostElement.ShowDialogAsync(
                EntryDialogTitle,
                SessionExpiredDialogDescription,
                "이동",
                "취소");
            if (contentDialogResult != ContentDialogResult.Primary) return null;

            NavigateToArcaconLoginPage(navigateAction, returnPageType, returnPageParameter);
            return null;
        }
    }

    public static async Task<bool> ShowArcaconLoginDialogAsync(UIElement dialogHostElement)
    {
        var loginInstructionTextBlock = new TextBlock
        {
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            Text = "아카라이브 로그인 후 자동으로 계속 진행합니다.",
            TextWrapping = TextWrapping.Wrap,
        };
        var loginWebView = new WebView2
        {
            MinHeight = 420,
        };
        var loginProgressRing = new ProgressRing
        {
            Width = 20,
            Height = 20,
            IsActive = true,
        };
        var loginStatusTextBlock = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            Text = "로그인 페이지를 준비하는 중...",
            TextWrapping = TextWrapping.Wrap,
        };
        var loginFooterGrid = new Grid
        {
            ColumnSpacing = 8,
        };
        loginFooterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        loginFooterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        loginFooterGrid.Children.Add(loginProgressRing);
        loginFooterGrid.Children.Add(loginStatusTextBlock);
        Grid.SetColumn(loginStatusTextBlock, 1);

        var loginContentGrid = new Grid
        {
            RowSpacing = 12,
        };
        loginContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        loginContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        loginContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        loginContentGrid.Children.Add(loginInstructionTextBlock);
        loginContentGrid.Children.Add(loginWebView);
        loginContentGrid.Children.Add(loginFooterGrid);
        Grid.SetRow(loginWebView, 1);
        Grid.SetRow(loginFooterGrid, 2);

        using var loginCancellationTokenSource = new CancellationTokenSource();
        var loginTaskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var isLoginSuccessful = false;
        Exception loginException = null;

        var loginContentDialog = new ContentDialog
        {
            Title = "아카콘 로그인",
            Content = loginContentGrid,
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = SettingsManager.GetElementTheme(),
            XamlRoot = dialogHostElement.XamlRoot,
        };

        loginContentDialog.Closing += (_, _) =>
        {
            if (!isLoginSuccessful && !loginCancellationTokenSource.IsCancellationRequested)
                loginCancellationTokenSource.Cancel();
        };
        loginContentDialog.Opened += async (_, _) =>
        {
            try
            {
                await loginWebView.EnsureCoreWebView2Async();
                if (loginWebView.CoreWebView2 is null) throw new InvalidOperationException("WebView2 초기화에 실패했습니다.");

                loginStatusTextBlock.Text = "아카라이브에 로그인해 주세요.";
                await App.ArcaconClient.LoginAsync(loginWebView.CoreWebView2, loginCancellationTokenSource.Token);

                if (loginCancellationTokenSource.IsCancellationRequested) return;

                isLoginSuccessful = true;
                loginStatusTextBlock.Text = "로그인 완료. 계속 진행하는 중...";
                loginContentDialog.Hide();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                loginException = exception;
                loginContentDialog.Hide();
            }
            finally
            {
                loginProgressRing.IsActive = false;
                loginTaskCompletionSource.TrySetResult(true);
            }
        };

        await loginContentDialog.ShowAsync();
        await loginTaskCompletionSource.Task;

        if (loginException is not null)
        {
            await dialogHostElement.ShowDialogAsync("아카콘 로그인 실패", loginException.Message);
            return false;
        }

        return isLoginSuccessful;
    }

    private static void NavigateToArcaconLoginPage(Action<Type, object> navigateAction, Type returnPageType, object returnPageParameter) => navigateAction(typeof(ArcaconLoginPage), new ArcaconLoginPageNavigationArguments(returnPageType, returnPageParameter));
}
