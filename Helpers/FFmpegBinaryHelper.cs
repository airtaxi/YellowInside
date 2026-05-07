using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace YellowInside.Helpers;

public static class FFmpegBinaryHelper
{
    private const string FFmpegDirectoryName = "ffmpeg";
    private const string FFmpegExecutableFileName = "ffmpeg.exe";
    private const string LocalCacheDirectoryName = "Local";
    private const string ApplicationDataDirectoryName = "YellowInside";
    private const int StreamCopyBufferSize = 81920;
    private const string Win32DownloadAddress = "https://github.com/defisym/FFmpeg-Builds-Win32/releases/download/latest/ffmpeg-master-latest-win32-lgpl-shared.zip";
    private const string WinArm64DownloadAddress = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-winarm64-lgpl-shared.zip";
    private const string Win64DownloadAddress = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip";

    private static readonly HttpClient s_httpClient = new();
    private static readonly SemaphoreSlim s_downloadSemaphore = new(1, 1);

    public static string FFmpegDirectoryPath => Path.Combine(
        ApplicationData.Current.LocalCacheFolder.Path,
        LocalCacheDirectoryName,
        ApplicationDataDirectoryName,
        FFmpegDirectoryName);
    public static string FFmpegBinaryPath => Path.Combine(FFmpegDirectoryPath, FFmpegExecutableFileName);

    public static bool IsFFmpegBinaryAvailable() => File.Exists(FFmpegBinaryPath);

    public static Task<string> EnsureFFmpegBinaryAsync(CancellationToken cancellationToken = default)
        => EnsureFFmpegBinaryAsync(null, cancellationToken);

    public static async Task<string> EnsureFFmpegBinaryAsync(IProgress<FFmpegBinaryProgress> progress, CancellationToken cancellationToken = default)
    {
        if (IsFFmpegBinaryAvailable())
        {
            ReportProgress(progress, FFmpegBinaryProgressStage.Completed, 1, 1, FFmpegBinaryPath);
            return FFmpegBinaryPath;
        }

        await s_downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsFFmpegBinaryAvailable())
            {
                ReportProgress(progress, FFmpegBinaryProgressStage.Completed, 1, 1, FFmpegBinaryPath);
                return FFmpegBinaryPath;
            }

            await DownloadFFmpegBinaryAsync(progress, cancellationToken);
            ReportProgress(progress, FFmpegBinaryProgressStage.Completed, 1, 1, FFmpegBinaryPath);
            return FFmpegBinaryPath;
        }
        finally
        {
            s_downloadSemaphore.Release();
        }
    }

    private static async Task DownloadFFmpegBinaryAsync(IProgress<FFmpegBinaryProgress> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(FFmpegDirectoryPath);

        var temporaryDirectoryPath = Path.Combine(Path.GetTempPath(), "YellowInside", nameof(FFmpegBinaryHelper), Guid.NewGuid().ToString("N"));
        var temporaryArchivePath = Path.Combine(temporaryDirectoryPath, "ffmpeg.zip");
        var temporaryExtractionDirectoryPath = Path.Combine(temporaryDirectoryPath, "extracted");

        Directory.CreateDirectory(temporaryDirectoryPath);
        Directory.CreateDirectory(temporaryExtractionDirectoryPath);

        try
        {
            await DownloadArchiveAsync(GetArchitectureDownloadAddress(), temporaryArchivePath, progress, cancellationToken);
            await ExtractArchiveAsync(temporaryArchivePath, temporaryExtractionDirectoryPath, progress, cancellationToken);
            CopyExtractedBinaryFiles(temporaryExtractionDirectoryPath);

            if (!IsFFmpegBinaryAvailable()) throw new FileNotFoundException("FFmpeg 바이너리를 찾을 수 없습니다.", FFmpegBinaryPath);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectoryPath);
        }
    }

    private static async Task DownloadArchiveAsync(string downloadAddress, string temporaryArchivePath, IProgress<FFmpegBinaryProgress> progress, CancellationToken cancellationToken)
    {
        using var response = await s_httpClient.GetAsync(downloadAddress, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalByteCount = response.Content.Headers.ContentLength;
        var downloadedByteCount = 0L;
        var streamCopyBuffer = new byte[StreamCopyBufferSize];

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var archiveFileStream = new FileStream(temporaryArchivePath, FileMode.Create, FileAccess.Write, FileShare.None, StreamCopyBufferSize, true);
        ReportProgress(progress, FFmpegBinaryProgressStage.Downloading, downloadedByteCount, totalByteCount, temporaryArchivePath);

        while (true)
        {
            var readByteCount = await responseStream.ReadAsync(streamCopyBuffer, cancellationToken);
            if (readByteCount == 0) break;

            await archiveFileStream.WriteAsync(streamCopyBuffer.AsMemory(0, readByteCount), cancellationToken);
            downloadedByteCount += readByteCount;
            ReportProgress(progress, FFmpegBinaryProgressStage.Downloading, downloadedByteCount, totalByteCount, temporaryArchivePath);
        }
    }

    private static async Task ExtractArchiveAsync(string temporaryArchivePath, string temporaryExtractionDirectoryPath, IProgress<FFmpegBinaryProgress> progress, CancellationToken cancellationToken)
    {
        await using var archiveFileStream = new FileStream(temporaryArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamCopyBufferSize, true);
        using var compressedArchive = new ZipArchive(archiveFileStream, ZipArchiveMode.Read);
        var compressedArchiveEntries = compressedArchive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        var totalByteCount = compressedArchiveEntries.Sum(entry => entry.Length);
        var extractedByteCount = 0L;
        var destinationRootDirectoryPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(temporaryExtractionDirectoryPath));
        var streamCopyBuffer = new byte[StreamCopyBufferSize];

        ReportProgress(progress, FFmpegBinaryProgressStage.Extracting, extractedByteCount, totalByteCount, temporaryExtractionDirectoryPath);

        foreach (var compressedArchiveEntry in compressedArchiveEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationFilePath = Path.GetFullPath(Path.Combine(temporaryExtractionDirectoryPath, compressedArchiveEntry.FullName));
            if (!destinationFilePath.StartsWith(destinationRootDirectoryPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("압축 파일에 잘못된 상대 경로가 포함되어 있습니다.");

            var destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destinationDirectoryPath)) Directory.CreateDirectory(destinationDirectoryPath);

            await using var compressedArchiveEntryStream = compressedArchiveEntry.Open();
            await using var destinationFileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, StreamCopyBufferSize, true);

            while (true)
            {
                var readByteCount = await compressedArchiveEntryStream.ReadAsync(streamCopyBuffer, cancellationToken);
                if (readByteCount == 0) break;

                await destinationFileStream.WriteAsync(streamCopyBuffer.AsMemory(0, readByteCount), cancellationToken);
                extractedByteCount += readByteCount;
                ReportProgress(progress, FFmpegBinaryProgressStage.Extracting, extractedByteCount, totalByteCount, compressedArchiveEntry.FullName);
            }
        }

        ReportProgress(progress, FFmpegBinaryProgressStage.Extracting, totalByteCount, totalByteCount, temporaryExtractionDirectoryPath);
    }

    private static string GetArchitectureDownloadAddress()
        => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => Win32DownloadAddress,
            Architecture.Arm64 => WinArm64DownloadAddress,
            Architecture.X64 => Win64DownloadAddress,
            var processArchitecture => throw new PlatformNotSupportedException($"지원하지 않는 프로세스 아키텍처입니다: {processArchitecture}"),
        };

    private static void CopyExtractedBinaryFiles(string temporaryExtractionDirectoryPath)
    {
        var extractedFFmpegBinaryPath = Directory
            .EnumerateFiles(temporaryExtractionDirectoryPath, FFmpegExecutableFileName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(extractedFFmpegBinaryPath)) throw new FileNotFoundException("압축 파일에서 FFmpeg 바이너리를 찾을 수 없습니다.", FFmpegExecutableFileName);

        var extractedBinaryDirectoryPath = Path.GetDirectoryName(extractedFFmpegBinaryPath);
        if (string.IsNullOrEmpty(extractedBinaryDirectoryPath)) throw new DirectoryNotFoundException("압축 해제된 FFmpeg 바이너리 폴더를 찾을 수 없습니다.");

        CopyDirectoryFiles(extractedBinaryDirectoryPath, FFmpegDirectoryPath);
    }

    private static string EnsureTrailingDirectorySeparator(string directoryPath)
        => directoryPath.EndsWith(Path.DirectorySeparatorChar) ? directoryPath : $"{directoryPath}{Path.DirectorySeparatorChar}";

    private static void ReportProgress(IProgress<FFmpegBinaryProgress> progress, FFmpegBinaryProgressStage stage, long completedByteCount, long? totalByteCount, string currentFilePath)
        => progress?.Report(new FFmpegBinaryProgress(stage, completedByteCount, totalByteCount, CalculateProgressPercentage(completedByteCount, totalByteCount), currentFilePath));

    private static double? CalculateProgressPercentage(long completedByteCount, long? totalByteCount)
        => totalByteCount > 0 ? Math.Clamp(completedByteCount * 100d / totalByteCount.Value, 0d, 100d) : null;

    private static void CopyDirectoryFiles(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            var relativeFilePath = Path.GetRelativePath(sourceDirectoryPath, sourceFilePath);
            var destinationFilePath = Path.Combine(destinationDirectoryPath, relativeFilePath);
            var destinationDirectoryPathForFile = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destinationDirectoryPathForFile)) Directory.CreateDirectory(destinationDirectoryPathForFile);

            File.Copy(sourceFilePath, destinationFilePath, true);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try { if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, true); }
        catch { }
    }
}

public enum FFmpegBinaryProgressStage
{
    Downloading,
    Extracting,
    Completed,
}

public sealed record FFmpegBinaryProgress(
    FFmpegBinaryProgressStage Stage,
    long CompletedByteCount,
    long? TotalByteCount,
    double? ProgressPercentage,
    string CurrentFilePath);
