using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.WebUI;
using WinRT.Interop;

namespace YellowInside.Helpers;

public static class DialogHelper
{
    public static async Task<(ContentDialogResult Result, string Text)> ShowInputDialogAsync(this UIElement element, string title = "입력", string placeholderText = "", string primaryText = "확인", string secondaryText = null, string cancelText = null, bool numberOnly = false, string defaultText = "")
    {
        HideOpenContentDialogs(element);

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = primaryText,
            DefaultButton = ContentDialogButton.Primary,
            Style = GetDefaultContentDialogStyle(),
            XamlRoot = element.XamlRoot,
            RequestedTheme = SettingsManager.GetElementTheme(),
        };
        var textBox = new TextBox();
        if (numberOnly) textBox.BeforeTextChanging += (_, e) => e.Cancel = e.NewText.Any(c => !char.IsDigit(c));
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBox.PlaceholderText = placeholderText;
        if (!string.IsNullOrEmpty(defaultText)) textBox.Text = defaultText;
        dialog.Content = textBox;
        if (!string.IsNullOrEmpty(secondaryText)) dialog.SecondaryButtonText = secondaryText;
        if (!string.IsNullOrEmpty(cancelText)) dialog.CloseButtonText = cancelText;
        var taskCompletionSource = new TaskCompletionSource<(ContentDialogResult Result, string Text)>();
        dialog.Closing += (_, args) =>
        {
            taskCompletionSource.SetResult((args.Result, args.Result == ContentDialogResult.Primary ? textBox.Text.Trim() : null));
        };
        await dialog.ShowAsync();
        return await taskCompletionSource.Task;
    }

    public static ContentDialog GenerateMessageDialog(this UIElement element, string title, string description, string primaryButtonText = "확인", string secondaryButtonText = null)
    {
        HideOpenContentDialogs(element);

        var xamlRoot = element.XamlRoot;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = description,
            PrimaryButtonText = primaryButtonText,
            XamlRoot = xamlRoot,
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = SettingsManager.GetElementTheme(),
            Style = GetDefaultContentDialogStyle(),
        };

        if (!string.IsNullOrEmpty(secondaryButtonText)) dialog.SecondaryButtonText = secondaryButtonText;
        return dialog;
    }

    public static async Task<ContentDialogResult> ShowDialogAsync(this UIElement element, string title, string description, string primaryButtonText = "확인", string secondaryButtonText = null)
    {
        var dialog = GenerateMessageDialog(element, title, description, primaryButtonText, secondaryButtonText);
        return await dialog.ShowAsync();
    }

    private static void HideOpenContentDialogs(UIElement element)
    {
        var contentDialogs = VisualTreeHelper.GetOpenPopupsForXamlRoot(element.XamlRoot).Where(x => x.Child is ContentDialog).Select(x => x.Child as ContentDialog);
        if (!contentDialogs.Any()) return;

        foreach (var contentDialog in contentDialogs) contentDialog.Hide();
    }

    private static Style GetDefaultContentDialogStyle() => Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) ? style as Style : null;
}
