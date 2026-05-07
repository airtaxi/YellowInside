using ImageMagick;
using ImageMagick.Formats;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;

namespace YellowInside.Managers;

public static class AnimatedPngToWebpConversionManager
{
    public const uint DefaultWebpQuality = 80;

    private const string AnimatedPngFormatPrefix = "apng:";
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static ReadOnlySpan<byte> AnimatedPngControlChunkType => "acTL"u8;
    private static ReadOnlySpan<byte> ImageDataChunkType => "IDAT"u8;

    public static bool IsAnimatedPng(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) return false;
        if (!Path.GetExtension(sourceFilePath).Equals(".png", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            if (!HasAnimatedPngControlChunk(sourceFilePath)) return false;

            using var imageCollection = ReadAnimatedPngImageCollection(sourceFilePath);
            return imageCollection.Count > 1;
        }
        catch (Exception exception)
        {
            App.LogException("AnimatedPngToWebpValidation", exception);
            return false;
        }
    }

    public static void ConvertAnimatedPngToWebp(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("원본 PNG 파일을 찾을 수 없습니다.", sourceFilePath);
        if (!Path.GetExtension(sourceFilePath).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("PNG 파일이 아닙니다.");
        if (!HasAnimatedPngControlChunk(sourceFilePath)) throw new InvalidOperationException("APNG 파일이 아닙니다.");

        using var imageCollection = ReadAnimatedPngImageCollection(sourceFilePath);
        if (imageCollection.Count <= 1) throw new InvalidOperationException("APNG 프레임을 확인할 수 없습니다.");

        imageCollection.Coalesce();
        foreach (var image in imageCollection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            image.Format = MagickFormat.WebP;
            image.Quality = DefaultWebpQuality;
        }

        var temporaryDestinationFilePath = $"{destinationFilePath}.{Guid.NewGuid():N}.tmp.webp";
        try
        {
            var destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destinationDirectoryPath)) Directory.CreateDirectory(destinationDirectoryPath);

            var webpWriteDefines = new WebPWriteDefines
            {
                AlphaQuality = (int)DefaultWebpQuality,
                Lossless = false,
                Method = 4,
                ThreadLevel = true,
                UseSharpYuv = true,
            };

            imageCollection.Write(temporaryDestinationFilePath, webpWriteDefines);
            ValidateConvertedWebp(temporaryDestinationFilePath);
            File.Move(temporaryDestinationFilePath, destinationFilePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryDestinationFilePath)) File.Delete(temporaryDestinationFilePath); }
            catch { }
        }
    }

    private static void ValidateConvertedWebp(string destinationFilePath)
    {
        using var imageCollection = new MagickImageCollection(destinationFilePath, MagickFormat.WebP);
        if (imageCollection.Count == 0) throw new InvalidOperationException("변환된 WebP 파일을 확인할 수 없습니다.");
    }

    private static MagickImageCollection ReadAnimatedPngImageCollection(string sourceFilePath)
    {
        var imageCollection = new MagickImageCollection();
        imageCollection.Read($"{AnimatedPngFormatPrefix}{sourceFilePath}");
        return imageCollection;
    }

    private static bool HasAnimatedPngControlChunk(string sourceFilePath)
    {
        Span<byte> signature = stackalloc byte[8];
        Span<byte> chunkHeader = stackalloc byte[8];

        using var stream = File.OpenRead(sourceFilePath);
        if (!TryReadExactly(stream, signature)) return false;
        if (!signature.SequenceEqual(PngSignature)) return false;

        while (stream.Position < stream.Length)
        {
            if (!TryReadExactly(stream, chunkHeader)) return false;

            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            var chunkType = chunkHeader[4..8];

            if (chunkType.SequenceEqual(AnimatedPngControlChunkType)) return true;
            if (chunkType.SequenceEqual(ImageDataChunkType)) return false;

            var bytesToSkip = checked((long)chunkLength + 4);
            if (stream.Length - stream.Position < bytesToSkip) return false;
            stream.Seek(bytesToSkip, SeekOrigin.Current);
        }

        return false;
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        var totalReadByteCount = 0;
        while (totalReadByteCount < buffer.Length)
        {
            var readByteCount = stream.Read(buffer[totalReadByteCount..]);
            if (readByteCount == 0) return false;
            totalReadByteCount += readByteCount;
        }

        return true;
    }
}
