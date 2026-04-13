using Microsoft.UI.Xaml;
using Windows.Storage;

namespace YellowInside;

public enum AppThemeSetting
{
    System,
    Light,
    Dark,
}

public static class SettingsManager
{
    private const string GifPlaybackKey = "GifPlaybackEnabled";
    private const string ThemeKey = "AppTheme";

    private static readonly ApplicationDataContainer s_localSettings =
        ApplicationData.Current.LocalSettings;

    public static bool GifPlaybackEnabled
    {
        get => s_localSettings.Values[GifPlaybackKey] is bool value && value;
        set => s_localSettings.Values[GifPlaybackKey] = value;
    }

    public static AppThemeSetting Theme
    {
        get => s_localSettings.Values[ThemeKey] is int value ? (AppThemeSetting)value : AppThemeSetting.System;
        set => s_localSettings.Values[ThemeKey] = (int)value;
    }

    public static ElementTheme GetElementTheme() => Theme switch
    {
        AppThemeSetting.Light => ElementTheme.Light,
        AppThemeSetting.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
