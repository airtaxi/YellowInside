using YellowInside.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading;
using System.Threading.Tasks;
using YellowInside.Models;

namespace YellowInside.Pages.Manage.Arcacon;

public sealed partial class ArcaconLoginPage : Page
{
    private ArcaconLoginPageNavigationArguments _navigationArguments = new(typeof(ArcaconHomePage), null);
    private CancellationTokenSource _loginCancellationTokenSource;
    private bool _isLoggingIn;

    public ArcaconLoginPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs navigationEventArgs)
    {
        base.OnNavigatedTo(navigationEventArgs);

        _navigationArguments = navigationEventArgs.Parameter as ArcaconLoginPageNavigationArguments
            ?? new ArcaconLoginPageNavigationArguments(typeof(ArcaconHomePage), null);
        await StartLoginAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs navigationEventArgs)
    {
        _loginCancellationTokenSource?.Cancel();
        _loginCancellationTokenSource?.Dispose();
        _loginCancellationTokenSource = null;

        base.OnNavigatedFrom(navigationEventArgs);
    }

    private async Task StartLoginAsync()
    {
        if (_isLoggingIn) return;

        _isLoggingIn = true;
        _loginCancellationTokenSource?.Cancel();
        _loginCancellationTokenSource?.Dispose();
        _loginCancellationTokenSource = new CancellationTokenSource();

        RetryButton.IsEnabled = false;
        LoginProgressRing.IsActive = true;
        StatusTextBlock.Text = "로그인 페이지를 준비하는 중...";

        try
        {
            await LoginWebView2.EnsureCoreWebView2Async();
            if (LoginWebView2.CoreWebView2 is null) throw new InvalidOperationException("WebView2 초기화에 실패했습니다.");

            StatusTextBlock.Text = "아카라이브에 로그인해 주세요.";
            await App.ArcaconClient.LoginAsync(LoginWebView2.CoreWebView2, _loginCancellationTokenSource.Token);

            if (_loginCancellationTokenSource.IsCancellationRequested) return;

            StatusTextBlock.Text = "로그인 완료. 페이지로 이동하는 중...";
            Frame.Navigate(_navigationArguments.ReturnPageType, _navigationArguments.ReturnPageParameter);
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = "로그인이 취소되었습니다."; }
        catch (Exception exception) 
        {
            StatusTextBlock.Text = "로그인에 실패했습니다. 다시 시도해 주세요.";
            await this.ShowDialogAsync("아카콘 로그인 실패", exception.Message);
        }
        finally
        {
            LoginProgressRing.IsActive = false;
            RetryButton.IsEnabled = true;
            _isLoggingIn = false;
        }
    }

    private async void OnRetryButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await StartLoginAsync();
}
