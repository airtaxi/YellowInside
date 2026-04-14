using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using YellowInside.Models;
using Windows.Storage;

namespace YellowInside.Managers;

/// <summary>
/// 최근 사용한 스티커 히스토리를 관리합니다. 최대 100개의 고유 스티커를 기록합니다.
/// </summary>
public static class HistoryManager
{
    private const string SettingsKey = "StickerHistory";
    private const int MaxHistoryCount = 100;

    private static readonly Lock s_lock = new();
    private static readonly ApplicationDataContainer s_localSettings =
        ApplicationData.Current.LocalSettings;

    private static List<HistoryEntry> s_entries = [];

    /// <summary>
    /// 히스토리를 LocalSettings에서 로드합니다.
    /// </summary>
    public static void Initialize()
    {
        lock (s_lock)
        {
            s_entries = Load();
        }
    }

    /// <summary>
    /// 스티커를 히스토리에 기록합니다. 이미 존재하면 맨 앞으로 이동합니다.
    /// </summary>
    public static void Record(ContentSource source, int packageIndex, string stickerPath)
    {
        lock (s_lock)
        {
            s_entries.RemoveAll(
                entry => entry.Source == source
                    && entry.PackageIndex == packageIndex
                    && entry.StickerPath == stickerPath);

            s_entries.Insert(0, new HistoryEntry
            {
                Source = source,
                PackageIndex = packageIndex,
                StickerPath = stickerPath,
            });

            if (s_entries.Count > MaxHistoryCount)
                s_entries.RemoveRange(MaxHistoryCount, s_entries.Count - MaxHistoryCount);
        }

        Save();
    }

    /// <summary>
    /// 히스토리 목록을 반환합니다 (최근 순).
    /// </summary>
    public static IReadOnlyList<HistoryEntry> GetEntries()
    {
        lock (s_lock)
        {
            return [.. s_entries];
        }
    }

    /// <summary>
    /// 특정 패키지에 속한 히스토리 항목을 모두 삭제합니다.
    /// </summary>
    public static void RemoveByPackage(ContentSource source, int packageIndex)
    {
        bool removed;
        lock (s_lock)
        {
            removed = s_entries.RemoveAll(
                entry => entry.Source == source && entry.PackageIndex == packageIndex) > 0;
        }

        if (removed) Save();
    }

    private static List<HistoryEntry> Load()
    {
        if (s_localSettings.Values[SettingsKey] is string json)
        {
            return JsonSerializer.Deserialize(json, HistoryManagerJsonContext.Default.ListHistoryEntry)
                ?? [];
        }
        return [];
    }

    private static void Save()
    {
        string json;
        lock (s_lock)
        {
            json = JsonSerializer.Serialize(s_entries, HistoryManagerJsonContext.Default.ListHistoryEntry);
        }
        s_localSettings.Values[SettingsKey] = json;
    }
}
