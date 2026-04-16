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
    private const string HotkeyEnabledKey = "HotkeyEnabled";
    private const string HotkeyModifiersKey = "HotkeyModifiers";
    private const string HotkeyKeyKey = "HotkeyKey";
    private const string LastSeenVersionKey = "LastSeenVersion";

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

    public static bool HotkeyEnabled
    {
        get => s_localSettings.Values[HotkeyEnabledKey] is bool value && value;
        set => s_localSettings.Values[HotkeyEnabledKey] = value;
    }

    // Default: Ctrl+Shift (0x0002 | 0x0004 = 0x0006)
    public static uint HotkeyModifiers
    {
        get => s_localSettings.Values[HotkeyModifiersKey] is int value
            ? (uint)value
            : HotkeyManager.ModifierControl | HotkeyManager.ModifierShift;
        set => s_localSettings.Values[HotkeyModifiersKey] = (int)value;
    }

    // Default: 0x44 (D)
    public static uint HotkeyKey
    {
        get => s_localSettings.Values[HotkeyKeyKey] is int value ? (uint)value : 0x44;
        set => s_localSettings.Values[HotkeyKeyKey] = (int)value;
    }

    public static string LastSeenVersion
    {
        get => s_localSettings.Values[LastSeenVersionKey] as string;
        set => s_localSettings.Values[LastSeenVersionKey] = value;
    }
}
