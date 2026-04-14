using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using YellowInside.Models;
using Windows.Storage;

namespace YellowInside.Managers;

/// <summary>
/// 앱(프로세스)별 전송 방식 호환성 모드 설정을 관리합니다.
/// true = 호환성 모드 (SendMethod.Clipboard), false = 자동 모드 (SendMethod.Auto)
/// </summary>
public static class AppSendMethodManager
{
    private const string SettingsKey = "AppSendMethodSettings";

    private static readonly ApplicationDataContainer s_localSettings =
        ApplicationData.Current.LocalSettings;

    /// <summary>
    /// 지정된 앱 타이틀의 호환성 모드 설정을 반환합니다. 기본값은 true (호환성 모드).
    /// </summary>
    public static bool GetCompatibilityMode(string applicationTitle)
    {
        var settings = LoadSettings();
        return !settings.TryGetValue(applicationTitle, out var value) || value;
    }

    /// <summary>
    /// 지정된 앱 타이틀의 호환성 모드 설정을 저장합니다.
    /// </summary>
    public static void SetCompatibilityMode(string applicationTitle, bool isCompatibilityMode)
    {
        var settings = LoadSettings();
        settings[applicationTitle] = isCompatibilityMode;
        SaveSettings(settings);
    }

    /// <summary>
    /// 현재 앱별 전송 방식 설정을 .yic 파일로 내보냅니다.
    /// </summary>
    public static async Task ExportAsync(string destinationFilePath)
    {
        var settings = LoadSettings();
        var json = JsonSerializer.Serialize(settings, AppSendMethodSettingsJsonContext.Default.DictionaryStringBoolean);
        await File.WriteAllTextAsync(destinationFilePath, json);
    }

    /// <summary>
    /// .yic 파일에서 앱별 전송 방식 설정을 불러옵니다.
    /// </summary>
    /// <param name="sourceFilePath">불러올 .yic 파일 경로</param>
    /// <param name="replaceAll">true이면 기존 설정을 대체, false이면 기존 설정에 병합 (파일 쪽 우선)</param>
    public static async Task ImportAsync(string sourceFilePath, bool replaceAll)
    {
        var json = await File.ReadAllTextAsync(sourceFilePath);
        var imported = JsonSerializer.Deserialize(json, AppSendMethodSettingsJsonContext.Default.DictionaryStringBoolean)
            ?? [];

        if (replaceAll)
        {
            SaveSettings(imported);
            return;
        }

        var existing = LoadSettings();
        foreach (var pair in imported)
            existing[pair.Key] = pair.Value;

        SaveSettings(existing);
    }

    private static Dictionary<string, bool> LoadSettings()
    {
        if (s_localSettings.Values[SettingsKey] is string json)
        {
            return JsonSerializer.Deserialize(json, AppSendMethodSettingsJsonContext.Default.DictionaryStringBoolean)
                ?? [];
        }
        return [];
    }

    private static void SaveSettings(Dictionary<string, bool> settings)
    {
        s_localSettings.Values[SettingsKey] =
            JsonSerializer.Serialize(settings, AppSendMethodSettingsJsonContext.Default.DictionaryStringBoolean);
    }
}
