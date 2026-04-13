using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using dccon.NET;
using YellowInside.Messages;
using YellowInside.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Authentication.OnlineId;

namespace YellowInside;

public static class ContentsManager
{
	private static readonly Lock s_lock = new();
	private static readonly HttpClient s_httpClient = new();
	private static readonly DcconClient s_dcconClient = new(s_httpClient);

	private static ContentsManagerData s_data = new();
	private static string s_basePath = string.Empty;
	private static string s_dataFilePath = string.Empty;

	/// <summary>패키지 목록이 변경되었을 때 발생</summary>
	public static event Action PackagesChanged;

	/// <summary>즐겨찾기 목록이 변경되었을 때 발생</summary>
	public static event Action FavoritesChanged;

	/// <summary>
	/// 매니저를 초기화하고 영속 데이터를 로드합니다.
	/// </summary>
	public static async Task InitializeAsync()
	{
		s_basePath = Path.Combine(
			Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path,
			"Local",
			"YellowInside");
		s_dataFilePath = Path.Combine(s_basePath, "contents.json");

		Directory.CreateDirectory(s_basePath);

		if (File.Exists(s_dataFilePath))
		{
			var json = await File.ReadAllTextAsync(s_dataFilePath);
			var deserialized = JsonSerializer.Deserialize(
				json,
				ContentsManagerJsonContext.Default.ContentsManagerData);
			if (deserialized is not null) s_data = deserialized;
		}
	}

	/// <summary>
	/// 디시콘 패키지를 다운로드합니다 (이미지 포함).
	/// </summary>
	public static async Task DownloadDcconPackageAsync(
		int packageIndex,
		IProgress<(int Completed, int Total)> progress = null,
		CancellationToken cancellationToken = default)
	{
		lock (s_lock)
		{
			if (s_data.DownloadedPackages.Any(
				package => package.Source == ContentSource.Dccon && package.PackageIndex == packageIndex))
				return;
		}

		var detail = await s_dcconClient.GetPackageDetailAsync(packageIndex, cancellationToken);

		var packageDirectory = GetPackageDirectory(ContentSource.Dccon, packageIndex);
		Directory.CreateDirectory(packageDirectory);

		await s_dcconClient.DownloadPackageAsync(packageIndex, packageDirectory, progress, cancellationToken);

		var mainImageFileName = await DownloadMainImageAsync(
			ContentSource.Dccon, detail.MainImagePath, packageDirectory, cancellationToken);

		var localDirectoryName = DcconFileNameHelper.SanitizeFileName(detail.Title);

		var stickerPackage = new StickerPackage
		{
			Source = ContentSource.Dccon,
			PackageIndex = detail.PackageIndex,
			Title = detail.Title,
			Description = detail.Description,
			MainImagePath = detail.MainImagePath,
			MainImageFileName = mainImageFileName,
			SellerName = detail.SellerName,
			RegistrationDate = detail.RegistrationDate,
			LocalDirectoryName = localDirectoryName,
			LocalDirectoryPath = Path.Combine(packageDirectory, localDirectoryName),
			Stickers = [.. detail.Stickers.Select(sticker => new Sticker
			{
				Path = sticker.Path,
				Title = sticker.Title,
				Extension = sticker.Extension,
				SortNumber = sticker.SortNumber,
				ImageUrl = sticker.ImageUrl,
				FileName = DcconFileNameHelper.GetStickerFileName(sticker),
			})],
			Tags = [.. detail.Tags],
		};

		lock (s_lock)
		{
			s_data.DownloadedPackages.Add(stickerPackage);
		}

		await SaveAsync();

		PackagesChanged?.Invoke();
        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(ContentSource.Dccon, packageIndex));
    }

	/// <summary>
	/// 다운로드된 패키지 목록을 반환합니다.
	/// </summary>
	public static IReadOnlyList<StickerPackage> GetDownloadedPackages(ContentSource? source = null)
	{
        lock (s_lock)
        {
            return [.. s_data.DownloadedPackages.Where(package => source == null || package.Source == source)];
        }
    }

	/// <summary>
	/// 특정 패키지가 다운로드되어 있는지 확인합니다.
	/// </summary>
	public static bool IsPackageDownloaded(ContentSource source, int packageIndex)
	{
		lock (s_lock)
		{
			return s_data.DownloadedPackages.Any(
				package => package.Source == source && package.PackageIndex == packageIndex);
		}
	}

	/// <summary>
	/// 스티커를 즐겨찾기에 추가합니다.
	/// </summary>
	public static async Task AddFavoriteAsync(ContentSource source, int packageIndex, string stickerPath)
	{
		lock (s_lock)
		{
			var alreadyExists = s_data.Favorites.Any(
				favorite => favorite.Source == source
					&& favorite.PackageIndex == packageIndex
					&& favorite.StickerPath == stickerPath);
			if (alreadyExists) return;

			s_data.Favorites.Add(new FavoriteSticker
			{
				Source = source,
				PackageIndex = packageIndex,
				StickerPath = stickerPath,
			});
		}

		await SaveAsync();

		FavoritesChanged?.Invoke();
        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIndex));
    }

	/// <summary>
	/// 스티커를 즐겨찾기에서 제거합니다.
	/// </summary>
	public static async Task RemoveFavoriteAsync(ContentSource source, int packageIndex, string stickerPath)
	{
		bool removed;
		lock (s_lock)
		{
			removed = s_data.Favorites.RemoveAll(
				favorite => favorite.Source == source
					&& favorite.PackageIndex == packageIndex
					&& favorite.StickerPath == stickerPath) > 0;
		}

		if (!removed) return;

		await SaveAsync();

		FavoritesChanged?.Invoke();
        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIndex));
    }

	/// <summary>
	/// 특정 스티커가 즐겨찾기에 있는지 확인합니다.
	/// </summary>
	public static bool IsFavorite(ContentSource source, int packageIndex, string stickerPath)
	{
		lock (s_lock)
		{
			return s_data.Favorites.Any(
				favorite => favorite.Source == source
					&& favorite.PackageIndex == packageIndex
					&& favorite.StickerPath == stickerPath);
		}
	}

	/// <summary>
	/// 즐겨찾기 목록을 반환합니다.
	/// </summary>
	public static IReadOnlyList<FavoriteSticker> GetFavorites(ContentSource? source = null)
	{
		lock (s_lock)
		{
			return [.. s_data.Favorites.Where(favorite => source == null || favorite.Source == source)];
		}
	}

	/// <summary>
	/// 다운로드된 패키지 목록의 순서를 변경합니다.
	/// </summary>
	public static async Task ReorderPackagesAsync(IReadOnlyList<(ContentSource Source, int PackageIndex)> newOrder)
	{
		lock (s_lock)
		{
			var packagesByKey = s_data.DownloadedPackages.ToDictionary(
				package => (package.Source, package.PackageIndex));

			var reordered = new List<StickerPackage>(s_data.DownloadedPackages.Count);
			foreach (var key in newOrder)
			{
				if (packagesByKey.Remove(key, out var package))
					reordered.Add(package);
			}
			reordered.AddRange(packagesByKey.Values);

			s_data.DownloadedPackages = reordered;
		}

		await SaveAsync();
	}

	/// <summary>
	/// 다운로드된 패키지를 삭제합니다. 해당 패키지의 즐겨찾기도 함께 삭제됩니다.
	/// </summary>
	public static async Task DeletePackageAsync(ContentSource source, int packageIndex)
	{
		bool packageRemoved;
		bool favoritesRemoved;

		lock (s_lock)
		{
			packageRemoved = s_data.DownloadedPackages.RemoveAll(
				package => package.Source == source && package.PackageIndex == packageIndex) > 0;
			favoritesRemoved = s_data.Favorites.RemoveAll(
				favorite => favorite.Source == source && favorite.PackageIndex == packageIndex) > 0;
		}

		if (!packageRemoved) return;

		var packageDirectory = GetPackageDirectory(source, packageIndex);
		if (Directory.Exists(packageDirectory))
			Directory.Delete(packageDirectory, recursive: true);

		await SaveAsync();

		PackagesChanged?.Invoke();
		if (favoritesRemoved) FavoritesChanged?.Invoke();

        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIndex));
    }

	/// <summary>
	/// 스티커 이미지의 로컬 파일 경로를 반환합니다.
	/// </summary>
	public static string GetStickerImagePath(ContentSource source, int packageIndex, string localDirectoryName, string fileName)
		=> Path.Combine(GetPackageDirectory(source, packageIndex), localDirectoryName, fileName);

    /// <summary>
    /// 패키지의 메인 이미지 로컬 파일 경로를 반환합니다.
    /// </summary>
    public static string GetMainImagePath(ContentSource source, int packageIndex, string mainImageFileName)
		=> Path.Combine(GetPackageDirectory(source, packageIndex), mainImageFileName);

    private static string GetPackageDirectory(ContentSource source, int packageIndex)
		=> Path.Combine(s_basePath, source.ToString(), packageIndex.ToString());

	/// <summary>
	/// 메인 이미지를 다운로드하여 패키지 디렉토리에 저장합니다.
	/// </summary>
	private static async Task<string> DownloadMainImageAsync(
		ContentSource source,
		string mainImagePath,
		string packageDirectory,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(mainImagePath)) return string.Empty;

		var imageUrl = Utils.GetImageUrl(source, mainImagePath);
		var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
		if (source == ContentSource.Dccon)
			request.Headers.Add("Referer", "https://dccon.dcinside.com");

		var response = await s_httpClient.SendAsync(request, cancellationToken);
		response.EnsureSuccessStatusCode();

		var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
		var extension = contentType switch
		{
			"image/gif" => ".gif",
			"image/png" => ".png",
			"image/jpeg" => ".jpg",
			"image/webp" => ".webp",
			_ => ".png",
		};

		var mainImageFileName = $"main_image{extension}";
		var mainImageFilePath = Path.Combine(packageDirectory, mainImageFileName);

		await using var fileStream = File.Create(mainImageFilePath);
		await response.Content.CopyToAsync(fileStream, cancellationToken);

		return mainImageFileName;
	}

	private static async Task SaveAsync()
	{
		string json;
		lock (s_lock)
		{
			json = JsonSerializer.Serialize(s_data, ContentsManagerJsonContext.Default.ContentsManagerData);
		}

		var directory = Path.GetDirectoryName(s_dataFilePath);
		if (directory is not null) Directory.CreateDirectory(directory);

		await File.WriteAllTextAsync(s_dataFilePath, json);
	}
}
