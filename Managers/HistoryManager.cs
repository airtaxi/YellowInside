using System;
using System.Collections.Generic;
using System.IO;
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
    private const string HistoryFileName = "StickerHistory.json";
    private const int MaxHistoryCount = 100;

    private static readonly Lock s_lock = new();
    private static readonly string s_historyFilePath =
        Path.Combine(ApplicationData.Current.LocalFolder.Path, HistoryFileName);

    private static List<HistoryEntry> s_entries = [];

    /// <summary>
    /// 히스토리를 LocalSettings에서 로드합니다.
    /// </summary>
    public static void Initialize()
    {
        lock (s_lock)
        {
            s_entries = Load();
            MigratePackageIdentifiers();
        }
    }

    /// <summary>
    /// 스티커를 히스토리에 기록합니다. 이미 존재하면 맨 앞으로 이동합니다.
    /// </summary>
    public static void Record(ContentSource source, string packageIdentifier, string stickerPath)
    {
        lock (s_lock)
        {
            s_entries.RemoveAll(
                entry => entry.Source == source
                    && entry.PackageIdentifier == packageIdentifier
                    && entry.StickerPath == stickerPath);

            s_entries.Insert(0, new HistoryEntry
            {
                Source = source,
                PackageIdentifier = packageIdentifier,
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
    public static void RemoveByPackage(ContentSource source, string packageIdentifier)
    {
        bool removed;
        lock (s_lock)
        {
            removed = s_entries.RemoveAll(
                entry => entry.Source == source && entry.PackageIdentifier == packageIdentifier) > 0;
        }

        if (removed) Save();
    }

    private static void MigratePackageIdentifiers()
    {
        var migrated = false;
        foreach (var entry in s_entries)
        {
            if (!string.IsNullOrEmpty(entry.PackageIdentifier)) continue;

#pragma warning disable CS0618 // Obsolete
            entry.PackageIdentifier = entry.PackageIndex.ToString();
#pragma warning restore CS0618
            migrated = true;
        }

        if (migrated) Save();
    }

    private static List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(s_historyFilePath))
            {
                var json = File.ReadAllText(s_historyFilePath);
                return JsonSerializer.Deserialize(json, HistoryManagerJsonContext.Default.ListHistoryEntry)
                    ?? [];
            }
        }
        catch { }

        return [];
    }

    private static void Save()
    {
        string json;
        lock (s_lock)
        {
            json = JsonSerializer.Serialize(s_entries, HistoryManagerJsonContext.Default.ListHistoryEntry);
        }

        try { File.WriteAllText(s_historyFilePath, json); }
        catch { }
    }
}
