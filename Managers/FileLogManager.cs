using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Storage;

namespace YellowInside.Managers;

public static class FileLogManager
{
    private const int MaxLogEntries = 1000;
    private const string LogFileName = "app_logs.txt";

    private static readonly Lock s_lock = new();
    private static readonly string s_logFilePath =
        Path.Combine(ApplicationData.Current.LocalFolder.Path, LogFileName);

    public static string LogFilePath => s_logFilePath;

    public static void Write(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{level}] {message}";

        lock (s_lock)
        {
            try
            {
                var lines = File.Exists(s_logFilePath)
                    ? File.ReadAllLines(s_logFilePath).ToList()
                    : [];

                lines.Add(logEntry);

                if (lines.Count > MaxLogEntries)
                    lines = lines.Skip(lines.Count - MaxLogEntries).ToList();

                File.WriteAllLines(s_logFilePath, lines);
            }
            catch
            {
                // Logging failure should not crash the app
            }
        }
    }

    public static void WriteInfo(string message) => Write("INFO", message);
    public static void WriteWarn(string message) => Write("WARN", message);
    public static void WriteError(string message) => Write("ERROR", message);

    public static bool HasLogs()
    {
        lock (s_lock)
            return File.Exists(s_logFilePath) && new FileInfo(s_logFilePath).Length > 0;
    }
}
