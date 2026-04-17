using Arcacon.NET.Exceptions;
using Arcacon.NET.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using YellowInside.Models;
using YellowInside.Pages;
using YellowInside.Pages.Manage;
using YellowInside.Pages.Manage.Arcacon;

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

        return await PromptArcaconLoginPageNavigationAsync(dialogHostElement, targetPageType, targetPageParameter);
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
            await PromptArcaconLoginPageNavigationAsync(dialogHostElement, returnPageType, returnPageParameter, isSessionExpired: true);
            return null;
        }
    }

    public static async Task<bool> PromptArcaconLoginPageNavigationAsync(
        UIElement dialogHostElement,
        Type returnPageType,
        object returnPageParameter = null,
        bool isSessionExpired = false)
    {
        var contentDialogResult = await dialogHostElement.ShowDialogAsync(
            EntryDialogTitle,
            isSessionExpired ? SessionExpiredDialogDescription : EntryDialogDescription,
            "이동",
            "취소");
        if (contentDialogResult != ContentDialogResult.Primary) return false;

        NavigateToArcaconLoginPage(returnPageType, returnPageParameter);
        return true;
    }

    public static void NavigateToArcaconLoginPage(Type returnPageType, object returnPageParameter = null)
    {
        var (normalizedReturnPageType, normalizedReturnPageParameter) = NormalizeArcaconLoginReturnPage(returnPageType, returnPageParameter);
        ManageWindow.NavigateAndClearBackStack(
            typeof(ArcaconLoginPage),
            new ArcaconLoginPageNavigationArguments(normalizedReturnPageType, normalizedReturnPageParameter));
    }

    private static (Type ReturnPageType, object ReturnPageParameter) NormalizeArcaconLoginReturnPage(Type returnPageType, object returnPageParameter)
    {
        if (returnPageType == typeof(ArcaconHomePage))
        {
            return (
                typeof(ManagePage),
                new ManagePageNavigationArguments(
                    typeof(HomePage),
                    new HomePageNavigationArguments(OpenArcaconPage: true)));
        }

        if (returnPageType == typeof(ArcaconSearchPage))
        {
            return (
                typeof(ManagePage),
                new ManagePageNavigationArguments(
                    typeof(SearchPage),
                    new SearchPageNavigationArguments(OpenArcaconPage: true)));
        }

        if (returnPageType == typeof(SettingsPage))
        {
            return (
                typeof(ManagePage),
                new ManagePageNavigationArguments(
                    typeof(SettingsPage),
                    returnPageParameter));
        }

        return (returnPageType, returnPageParameter);
    }
}
