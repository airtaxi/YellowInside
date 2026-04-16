using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using dccon.NET;
using YellowInside.Messages;
using YellowInside.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
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

		if (MigratePackageIdentifiers())
			await SaveAsync();
	}

	/// <summary>
	/// 기존 int PackageIndex 기반 데이터를 string PackageIdentifier로 마이그레이션합니다.
	/// </summary>
	private static bool MigratePackageIdentifiers()
	{
		var migrated = false;

		foreach (var package in s_data.DownloadedPackages)
		{
			if (!string.IsNullOrEmpty(package.PackageIdentifier)) continue;

#pragma warning disable CS0618 // Obsolete
			package.PackageIdentifier = package.PackageIndex.ToString();
#pragma warning restore CS0618
			migrated = true;
		}

		foreach (var favorite in s_data.Favorites)
		{
			if (!string.IsNullOrEmpty(favorite.PackageIdentifier)) continue;

#pragma warning disable CS0618 // Obsolete
			favorite.PackageIdentifier = favorite.PackageIndex.ToString();
#pragma warning restore CS0618
			migrated = true;
		}

		return migrated;
	}

	/// <summary>
	/// 임포트된 데이터의 PackageIdentifier를 마이그레이션합니다.
	/// </summary>
	private static void MigrateImportedPackageIdentifiers(ContentsManagerData importedData)
	{
		foreach (var package in importedData.DownloadedPackages)
		{
			if (!string.IsNullOrEmpty(package.PackageIdentifier)) continue;

#pragma warning disable CS0618 // Obsolete
			package.PackageIdentifier = package.PackageIndex.ToString();
#pragma warning restore CS0618
		}

		foreach (var favorite in importedData.Favorites)
		{
			if (!string.IsNullOrEmpty(favorite.PackageIdentifier)) continue;

#pragma warning disable CS0618 // Obsolete
			favorite.PackageIdentifier = favorite.PackageIndex.ToString();
#pragma warning restore CS0618
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
		var packageIdentifier = packageIndex.ToString();

		lock (s_lock)
		{
			if (s_data.DownloadedPackages.Any(
				package => package.Source == ContentSource.Dccon && package.PackageIdentifier == packageIdentifier))
				return;
		}

		var detail = await s_dcconClient.GetPackageDetailAsync(packageIndex, cancellationToken);

		var packageDirectory = GetPackageDirectory(ContentSource.Dccon, packageIdentifier);
		Directory.CreateDirectory(packageDirectory);

		await s_dcconClient.DownloadPackageAsync(packageIndex, packageDirectory, progress, cancellationToken);

		var mainImageFileName = await DownloadMainImageAsync(
			ContentSource.Dccon, detail.MainImagePath, packageDirectory, cancellationToken);

		var localDirectoryName = DcconFileNameHelper.SanitizeFileName(detail.Title);

		var stickerPackage = new StickerPackage
		{
			Source = ContentSource.Dccon,
			PackageIdentifier = packageIdentifier,
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
        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(ContentSource.Dccon, packageIdentifier));
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
	public static bool IsPackageDownloaded(ContentSource source, string packageIdentifier)
	{
		lock (s_lock)
		{
			return s_data.DownloadedPackages.Any(
				package => package.Source == source && package.PackageIdentifier == packageIdentifier);
		}
	}

	/// <summary>
	/// 스티커를 즐겨찾기에 추가합니다.
	/// </summary>
	public static async Task AddFavoriteAsync(ContentSource source, string packageIdentifier, string stickerPath)
	{
		lock (s_lock)
		{
			var alreadyExists = s_data.Favorites.Any(
				favorite => favorite.Source == source
					&& favorite.PackageIdentifier == packageIdentifier
					&& favorite.StickerPath == stickerPath);
			if (alreadyExists) return;

			s_data.Favorites.Add(new FavoriteSticker
			{
				Source = source,
				PackageIdentifier = packageIdentifier,
				StickerPath = stickerPath,
			});
		}

		await SaveAsync();

		FavoritesChanged?.Invoke();
        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIdentifier));
    }

	/// <summary>
	/// 스티커를 즐겨찾기에서 제거합니다.
	/// </summary>
	public static async Task RemoveFavoriteAsync(ContentSource source, string packageIdentifier, string stickerPath)
	{
		bool removed;
		lock (s_lock)
		{
			removed = s_data.Favorites.RemoveAll(
				favorite => favorite.Source == source
					&& favorite.PackageIdentifier == packageIdentifier
					&& favorite.StickerPath == stickerPath) > 0;
		}

		if (!removed) return;

		await SaveAsync();

		FavoritesChanged?.Invoke();
        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIdentifier));
    }

	/// <summary>
	/// 특정 스티커가 즐겨찾기에 있는지 확인합니다.
	/// </summary>
	public static bool IsFavorite(ContentSource source, string packageIdentifier, string stickerPath)
	{
		lock (s_lock)
		{
			return s_data.Favorites.Any(
				favorite => favorite.Source == source
					&& favorite.PackageIdentifier == packageIdentifier
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
	public static async Task ReorderPackagesAsync(IReadOnlyList<(ContentSource Source, string PackageIdentifier)> newOrder)
	{
		lock (s_lock)
		{
			var packagesByKey = s_data.DownloadedPackages.ToDictionary(
				package => (package.Source, package.PackageIdentifier));

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
	public static async Task DeletePackageAsync(ContentSource source, string packageIdentifier)
	{
		bool packageRemoved;
		bool favoritesRemoved;

		lock (s_lock)
		{
			packageRemoved = s_data.DownloadedPackages.RemoveAll(
				package => package.Source == source && package.PackageIdentifier == packageIdentifier) > 0;
			favoritesRemoved = s_data.Favorites.RemoveAll(
				favorite => favorite.Source == source && favorite.PackageIdentifier == packageIdentifier) > 0;
		}

		if (!packageRemoved) return;

		Managers.HistoryManager.RemoveByPackage(source, packageIdentifier);

		var packageDirectory = GetPackageDirectory(source, packageIdentifier);
		if (Directory.Exists(packageDirectory))
			Directory.Delete(packageDirectory, recursive: true);

		await SaveAsync();

		PackagesChanged?.Invoke();
		if (favoritesRemoved) FavoritesChanged?.Invoke();

        WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIdentifier));
    }

	/// <summary>
	/// 스티커 이미지의 로컬 파일 경로를 반환합니다.
	/// </summary>
	public static string GetStickerImagePath(ContentSource source, string packageIdentifier, string localDirectoryName, string fileName)
		=> Path.Combine(GetPackageDirectory(source, packageIdentifier), localDirectoryName, fileName);

    /// <summary>
    /// 패키지의 메인 이미지 로컬 파일 경로를 반환합니다.
    /// </summary>
    public static string GetMainImagePath(ContentSource source, string packageIdentifier, string mainImageFileName)
		=> Path.Combine(GetPackageDirectory(source, packageIdentifier), mainImageFileName);

    private static string GetPackageDirectory(ContentSource source, string packageIdentifier)
		=> Path.Combine(s_basePath, source.ToString(), packageIdentifier);

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

	/// <summary>
	/// 현재 다운로드된 패키지와 설정을 .yip 파일로 내보냅니다.
	/// </summary>
	public static Task ExportAsync(string destinationFilePath, CancellationToken cancellationToken = default)
		=> ExportAsync(destinationFilePath, selectedPackageKeys: null, cancellationToken);

	/// <summary>
	/// 선택한 패키지와 관련 즐겨찾기를 .yip 파일로 내보냅니다.
	/// </summary>
	public static async Task ExportAsync(
		string destinationFilePath,
		IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> selectedPackageKeys,
		CancellationToken cancellationToken = default)
	{
		if (File.Exists(destinationFilePath))
			File.Delete(destinationFilePath);

		var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"YellowInside_Export_{Guid.NewGuid():N}");
		try
		{
			Directory.CreateDirectory(temporaryDirectory);

			string dataJson;
			List<StickerPackage> packages;
			HashSet<(ContentSource Source, string PackageIdentifier)> selectedPackageKeySet = null;
			lock (s_lock)
			{
				if (selectedPackageKeys is not null)
					selectedPackageKeySet = [.. selectedPackageKeys];

				var exportData = new ContentsManagerData
				{
					DownloadedPackages = selectedPackageKeySet is null
						? [.. s_data.DownloadedPackages]
						: [.. s_data.DownloadedPackages.Where(package => selectedPackageKeySet.Contains((package.Source, package.PackageIdentifier)))],
					Favorites = selectedPackageKeySet is null
						? [.. s_data.Favorites]
						: [.. s_data.Favorites.Where(favorite => selectedPackageKeySet.Contains((favorite.Source, favorite.PackageIdentifier)))],
				};

				dataJson = JsonSerializer.Serialize(exportData, ContentsManagerJsonContext.Default.ContentsManagerData);
				packages = exportData.DownloadedPackages;
			}

			await File.WriteAllTextAsync(
				Path.Combine(temporaryDirectory, "contents.json"), dataJson, cancellationToken);

			foreach (var package in packages)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var sourceDirectory = GetPackageDirectory(package.Source, package.PackageIdentifier);
				if (!Directory.Exists(sourceDirectory)) continue;

				var relativeDirectory = Path.Combine(package.Source.ToString(), package.PackageIdentifier);
				var targetDirectory = Path.Combine(temporaryDirectory, relativeDirectory);

				CopyDirectoryRecursive(sourceDirectory, targetDirectory);
			}

			ZipFile.CreateFromDirectory(temporaryDirectory, destinationFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
		}
		finally
		{
			if (Directory.Exists(temporaryDirectory))
				Directory.Delete(temporaryDirectory, recursive: true);
		}
	}

	/// <summary>
	/// .yip 파일에 포함된 패키지 목록을 읽어옵니다.
	/// </summary>
	/// <param name="sourceFilePath">불러올 .yip 파일 경로</param>
	/// <param name="cancellationToken">취소 토큰</param>
	public static async Task<IReadOnlyList<StickerPackage>> ReadPackagesFromImportFileAsync(
		string sourceFilePath,
		CancellationToken cancellationToken = default)
	{
		using var importArchive = ZipFile.OpenRead(sourceFilePath);
		var importedDataEntry = importArchive.GetEntry("contents.json")
			?? throw new InvalidOperationException("유효하지 않은 .yip 파일입니다. contents.json이 존재하지 않습니다.");

		using var importedDataStream = importedDataEntry.Open();
		var importedData = await DeserializeImportedDataAsync(importedDataStream, cancellationToken);
		MigrateImportedPackageIdentifiers(importedData);
		return importedData.DownloadedPackages;
	}

	/// <summary>
	/// .yip 파일을 불러옵니다.
	/// </summary>
	/// <param name="sourceFilePath">불러올 .yip 파일 경로</param>
	/// <param name="replaceAll">true이면 기존 데이터를 모두 삭제하고 새로 시작, false이면 기존 데이터에 추가만 합니다.</param>
	/// <param name="cancellationToken">취소 토큰</param>
	public static async Task ImportAsync(string sourceFilePath, bool replaceAll, CancellationToken cancellationToken = default)
	{
		var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"YellowInside_Import_{Guid.NewGuid():N}");
		try
		{
			ZipFile.ExtractToDirectory(sourceFilePath, temporaryDirectory);

			var importedDataFilePath = Path.Combine(temporaryDirectory, "contents.json");
			if (!File.Exists(importedDataFilePath)) throw new InvalidOperationException("유효하지 않은 .yip 파일입니다. contents.json이 존재하지 않습니다.");

			await using var importedDataStream = File.OpenRead(importedDataFilePath);
			var importedData = await DeserializeImportedDataAsync(importedDataStream, cancellationToken);
			MigrateImportedPackageIdentifiers(importedData);

            if (replaceAll)
			{
				lock (s_lock)
				{
					foreach (var package in s_data.DownloadedPackages)
					{
						var directory = GetPackageDirectory(package.Source, package.PackageIdentifier);
						if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
					}

					s_data = importedData;
				}

				foreach (var package in importedData.DownloadedPackages)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var importedPackageDirectory = Path.Combine(temporaryDirectory, package.Source.ToString(), package.PackageIdentifier);
					if (!Directory.Exists(importedPackageDirectory)) continue;

					var targetDirectory = GetPackageDirectory(package.Source, package.PackageIdentifier);
					CopyDirectoryRecursive(importedPackageDirectory, targetDirectory);
				}
			}
			else
			{
				lock (s_lock)
				{
					var existingKeys = s_data.DownloadedPackages.Select(package => (package.Source, package.PackageIdentifier)).ToHashSet();

					foreach (var package in importedData.DownloadedPackages)
					{
						if (existingKeys.Contains((package.Source, package.PackageIdentifier))) continue;
						s_data.DownloadedPackages.Add(package);
					}

					var existingFavoriteKeys = s_data.Favorites
						.Select(favorite => (favorite.Source, favorite.PackageIdentifier, favorite.StickerPath))
						.ToHashSet();

					foreach (var favorite in importedData.Favorites)
					{
						if (existingFavoriteKeys.Contains((favorite.Source, favorite.PackageIdentifier, favorite.StickerPath))) continue;
						s_data.Favorites.Add(favorite);
					}
				}

				foreach (var package in importedData.DownloadedPackages)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var targetDirectory = GetPackageDirectory(package.Source, package.PackageIdentifier);
					if (Directory.Exists(targetDirectory)) continue;

					var importedPackageDirectory = Path.Combine(
						temporaryDirectory, package.Source.ToString(), package.PackageIdentifier);
					if (!Directory.Exists(importedPackageDirectory)) continue;

					CopyDirectoryRecursive(importedPackageDirectory, targetDirectory);
				}
			}

			await SaveAsync();

			PackagesChanged?.Invoke();
			FavoritesChanged?.Invoke();

			ManageWindow.Instance.DispatcherQueue.TryEnqueue(() =>
			{
				foreach (var package in importedData.DownloadedPackages)
				{
					WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(package.Source, package.PackageIdentifier));
				}
			});
        }
		finally
		{
			if (Directory.Exists(temporaryDirectory))
			{
				Directory.Delete(temporaryDirectory, recursive: true);
			}
		}
	}

	private static async Task<ContentsManagerData> DeserializeImportedDataAsync(
		Stream importedDataStream,
		CancellationToken cancellationToken = default)
		=> await JsonSerializer.DeserializeAsync(
			importedDataStream,
			ContentsManagerJsonContext.Default.ContentsManagerData,
			cancellationToken)
		?? throw new InvalidOperationException("유효하지 않은 .yip 파일입니다. contents.json을 역직렬화할 수 없습니다.");

	private static void CopyDirectoryRecursive(string sourceDirectory, string targetDirectory)
	{
		Directory.CreateDirectory(targetDirectory);

		foreach (var file in Directory.GetFiles(sourceDirectory))
		{
			var destinationFile = Path.Combine(targetDirectory, Path.GetFileName(file));
			File.Copy(file, destinationFile, overwrite: true);
		}

		foreach (var subdirectory in Directory.GetDirectories(sourceDirectory))
		{
			var destinationSubdirectory = Path.Combine(targetDirectory, Path.GetFileName(subdirectory));
			CopyDirectoryRecursive(subdirectory, destinationSubdirectory);
		}
	}

	private static async Task SaveAsync()
	{
		string json;
		lock (s_lock) json = JsonSerializer.Serialize(s_data, ContentsManagerJsonContext.Default.ContentsManagerData);

		var directory = Path.GetDirectoryName(s_dataFilePath);
		if (directory is not null) Directory.CreateDirectory(directory);

		await File.WriteAllTextAsync(s_dataFilePath, json);
	}
}
