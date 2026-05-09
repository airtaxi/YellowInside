using Arcacon.NET;
using Arcacon.NET.Models;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using dccon.NET;
using dccon.NET.Models;
using InvenSticker.NET;
using InvenSticker.NET.Models;
using YellowInside.Messages;
using YellowInside.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Authentication.OnlineId;
using YellowInside.Managers;

namespace YellowInside;

public static class ContentsManager
{
	private const int StreamCopyBufferSize = 81920;

	private static readonly Lock s_lock = new();
	private static readonly HttpClient s_httpClient = new();
	private static readonly DcconClient s_dcconClient = new(s_httpClient);
	private static readonly InvenStickerClient s_stickerClient = new(s_httpClient);

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

		if (MigratePackageIdentifiers()) await SaveAsync();
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
	/// 사용자 지정 패키지를 추가합니다.
	/// </summary>
	public static async Task<string> AddCustomPackageAsync(
		string title,
		string description,
		string mainImageSourcePath,
		string sellerName,
		string registrationDate,
		IReadOnlyList<string> tags,
		IReadOnlyList<string> stickerSourcePaths)
	{
		var packageIdentifier = Guid.NewGuid().ToString();
		var packageDirectory = GetPackageDirectory(ContentSource.Local, packageIdentifier);
		Directory.CreateDirectory(packageDirectory);

		var stickersDirectory = Path.Combine(packageDirectory, "stickers");
		Directory.CreateDirectory(stickersDirectory);

		var mainImageFileName = string.Empty;
		if (!string.IsNullOrEmpty(mainImageSourcePath) && File.Exists(mainImageSourcePath))
		{
			mainImageFileName = $"main_image{Path.GetExtension(mainImageSourcePath)}";
			File.Copy(mainImageSourcePath, Path.Combine(packageDirectory, mainImageFileName));
		}

		var stickers = new List<Sticker>();
		for (var index = 0; index < stickerSourcePaths.Count; index++)
		{
			var sourcePath = stickerSourcePaths[index];
			if (!File.Exists(sourcePath)) continue;

			var extension = Path.GetExtension(sourcePath);
			var fileName = $"{Guid.NewGuid():N}{extension}";
			File.Copy(sourcePath, Path.Combine(stickersDirectory, fileName));

			stickers.Add(new Sticker
			{
				Path = fileName,
				Title = System.IO.Path.GetFileNameWithoutExtension(sourcePath),
				Extension = extension.TrimStart('.'),
				SortNumber = index,
				FileName = fileName,
			});
		}

		var stickerPackage = new StickerPackage
		{
			Source = ContentSource.Local,
			PackageIdentifier = packageIdentifier,
			Title = title,
			Description = description,
			MainImageFileName = mainImageFileName,
			SellerName = sellerName,
			RegistrationDate = registrationDate,
			Tags = [.. tags],
			LocalDirectoryName = "stickers",
			LocalDirectoryPath = stickersDirectory,
			Stickers = stickers,
		};

		lock (s_lock)
		{
			s_data.DownloadedPackages.Add(stickerPackage);
		}

		await SaveAsync();

		PackagesChanged?.Invoke();
		WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(ContentSource.Local, packageIdentifier));
		return packageIdentifier;
	}

	/// <summary>
	/// 사용자 지정 패키지를 수정합니다.
	/// </summary>
	public static async Task UpdateCustomPackageAsync(
		string packageIdentifier,
		string title,
		string description,
		string newMainImageSourcePath,
		string sellerName,
		IReadOnlyList<string> tags,
		IReadOnlyList<(string SourceFilePath, bool IsExisting, string OriginalStickerPath, string OriginalFileName)> stickerEntries)
	{
		StickerPackage package;
		lock (s_lock)
		{
			package = s_data.DownloadedPackages.FirstOrDefault(p => p.Source == ContentSource.Local && p.PackageIdentifier == packageIdentifier);
		}
		if (package is null) return;

		var packageDirectory = GetPackageDirectory(ContentSource.Local, packageIdentifier);
		var stickersDirectory = Path.Combine(packageDirectory, package.LocalDirectoryName);
		Directory.CreateDirectory(stickersDirectory);

		// Collect old sticker info for orphan cleanup and favorite/history pruning
		var oldStickerFileNames = package.Stickers.Select(sticker => sticker.FileName).ToHashSet();
		var oldStickerWebpFileNames = package.Stickers
			.Select(sticker => sticker.WebpFileName)
			.Where(webpFileName => !string.IsNullOrWhiteSpace(webpFileName))
			.ToHashSet();
		var oldStickerPaths = package.Stickers.Select(sticker => sticker.Path).ToHashSet();
		var keptStickerFileNames = new HashSet<string>();
		var keptStickerWebpFileNames = new HashSet<string>();
		var keptStickerPaths = new HashSet<string>();

		// Build new sticker list (copy new files first, before metadata update)
		var newStickers = new List<Sticker>();
		for (var index = 0; index < stickerEntries.Count; index++)
		{
			var entry = stickerEntries[index];
			if (entry.IsExisting)
			{
				keptStickerFileNames.Add(entry.OriginalFileName);
				keptStickerPaths.Add(entry.OriginalStickerPath);
				var existingSticker = package.Stickers.FirstOrDefault(sticker => sticker.Path == entry.OriginalStickerPath);
				if (existingSticker is not null)
				{
					if (!string.IsNullOrWhiteSpace(existingSticker.WebpFileName)) keptStickerWebpFileNames.Add(existingSticker.WebpFileName);
					newStickers.Add(new Sticker
					{
						Path = existingSticker.Path,
						Title = existingSticker.Title,
						Extension = existingSticker.Extension,
						SortNumber = index,
						FileName = existingSticker.FileName,
						WebpFileName = existingSticker.WebpFileName,
					});
				}
			}
			else
			{
				if (!File.Exists(entry.SourceFilePath)) continue;

				var extension = Path.GetExtension(entry.SourceFilePath);
				var fileName = $"{Guid.NewGuid():N}{extension}";
				File.Copy(entry.SourceFilePath, Path.Combine(stickersDirectory, fileName));

				newStickers.Add(new Sticker
				{
					Path = fileName,
					Title = System.IO.Path.GetFileNameWithoutExtension(entry.SourceFilePath),
					Extension = extension.TrimStart('.'),
					SortNumber = index,
					FileName = fileName,
				});
			}
		}

		// Handle main image (copy new before metadata update)
		var oldMainImageFileName = package.MainImageFileName;
		var mainImageFileName = package.MainImageFileName;
		if (!string.IsNullOrEmpty(newMainImageSourcePath) && File.Exists(newMainImageSourcePath))
		{
			mainImageFileName = $"main_image{Path.GetExtension(newMainImageSourcePath)}";
			File.Copy(newMainImageSourcePath, Path.Combine(packageDirectory, mainImageFileName), overwrite: true);
		}

		// Update metadata + save
		lock (s_lock)
		{
			package.Title = title;
			package.Description = description;
			package.MainImageFileName = mainImageFileName;
			package.SellerName = sellerName;
			package.Tags = [.. tags];
			package.Stickers = newStickers;
		}

		await SaveAsync();

		// Delete orphaned sticker files (after metadata is safely saved)
		foreach (var orphanedFileName in oldStickerFileNames.Except(keptStickerFileNames))
		{
			var orphanedFilePath = Path.Combine(stickersDirectory, orphanedFileName);
			try { if (File.Exists(orphanedFilePath)) File.Delete(orphanedFilePath); }
			catch { }
		}

		foreach (var orphanedWebpFileName in oldStickerWebpFileNames.Except(keptStickerWebpFileNames))
		{
			var orphanedWebpFilePath = Path.Combine(stickersDirectory, orphanedWebpFileName);
			try { if (File.Exists(orphanedWebpFilePath)) File.Delete(orphanedWebpFilePath); }
			catch { }
		}

		// Delete old main image if replaced with different extension
		if (!string.IsNullOrEmpty(newMainImageSourcePath) &&
			!string.IsNullOrEmpty(oldMainImageFileName) &&
			oldMainImageFileName != mainImageFileName)
		{
			var oldMainImagePath = Path.Combine(packageDirectory, oldMainImageFileName);
			try { if (File.Exists(oldMainImagePath)) File.Delete(oldMainImagePath); }
			catch { }
		}

		// Remove favorites and history entries for deleted stickers
		var removedStickerPaths = oldStickerPaths.Except(keptStickerPaths).ToList();
		if (removedStickerPaths.Count > 0)
		{
			bool favoritesRemoved;
			lock (s_lock)
			{
				favoritesRemoved = s_data.Favorites.RemoveAll(
					favorite => favorite.Source == ContentSource.Local
						&& favorite.PackageIdentifier == packageIdentifier
						&& removedStickerPaths.Contains(favorite.StickerPath)) > 0;
			}

			HistoryManager.RemoveByStickers(ContentSource.Local, packageIdentifier, removedStickerPaths);

			if (favoritesRemoved) FavoritesChanged?.Invoke();
		}

		PackagesChanged?.Invoke();
		WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(ContentSource.Local, packageIdentifier));
	}
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

	public static async Task DownloadArcaconPackageAsync(
		int packageIndex,
		IProgress<(int Completed, int Total)> progress = null,
		CancellationToken cancellationToken = default)
	{
		var packageIdentifier = packageIndex.ToString();

		lock (s_lock)
		{
			if (s_data.DownloadedPackages.Any(
				package => package.Source == ContentSource.Arcacon && package.PackageIdentifier == packageIdentifier))
				return;
		}

		var detail = await App.ArcaconClient.GetPackageDetailAsync(packageIndex, cancellationToken);

		var packageDirectory = GetPackageDirectory(ContentSource.Arcacon, packageIdentifier);
		Directory.CreateDirectory(packageDirectory);

		var localDirectoryName = ArcaconFileNameHelper.SanitizeFileName(detail.Title);
		var stickerDirectory = Path.Combine(packageDirectory, localDirectoryName);
		Directory.CreateDirectory(stickerDirectory);
		var stickers = await DownloadArcaconStickerFilesAsync(detail.Stickers, stickerDirectory, progress, cancellationToken);

		var mainImagePath = $"https://arca.live/api/emoticon/{packageIndex}/thumb";
		var mainImageFileName = await DownloadMainImageAsync(
			ContentSource.Arcacon, mainImagePath, packageDirectory, cancellationToken);

		var stickerPackage = new StickerPackage
		{
			Source = ContentSource.Arcacon,
			PackageIdentifier = packageIdentifier,
			Title = detail.Title,
			Description = detail.Price == 0 ? string.Empty : $"{detail.Price}pt",
			MainImagePath = mainImagePath,
			MainImageFileName = mainImageFileName,
			SellerName = detail.SellerName,
			RegistrationDate = detail.RegistrationDate,
			LocalDirectoryName = localDirectoryName,
			LocalDirectoryPath = stickerDirectory,
			Stickers = stickers,
			Tags = [.. detail.Tags],
		};

		lock (s_lock)
		{
			s_data.DownloadedPackages.Add(stickerPackage);
		}

		await SaveAsync();

		PackagesChanged?.Invoke();
		WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(ContentSource.Arcacon, packageIdentifier));
	}

	public static async Task DownloadInvenStickerPackageAsync(
		int packageId,
		IProgress<(int Completed, int Total)> progress = null,
		CancellationToken cancellationToken = default)
	{
		var packageIdentifier = packageId.ToString();

		lock (s_lock)
		{
			if (s_data.DownloadedPackages.Any(
				package => package.Source == ContentSource.Inven && package.PackageIdentifier == packageIdentifier))
				return;
		}

		var detail = await s_stickerClient.GetDetailAsync(packageId, cancellationToken);

		var packageDirectory = GetPackageDirectory(ContentSource.Inven, packageIdentifier);
		Directory.CreateDirectory(packageDirectory);

		await s_stickerClient.DownloadPackageAsync(packageId, packageDirectory, progress, cancellationToken);

		var mainImageFileName = await DownloadMainImageAsync(
			ContentSource.Inven, detail.ThumbnailUrl, packageDirectory, cancellationToken);

		var localDirectoryName = InvenStickerFileNameHelper.SanitizeFileName(detail.Title);

		var stickerPackage = new StickerPackage
		{
			Source = ContentSource.Inven,
			PackageIdentifier = packageIdentifier,
			Title = detail.Title,
			Description = detail.PriceInfo,
			MainImagePath = detail.ThumbnailUrl,
			MainImageFileName = mainImageFileName,
			SellerName = detail.AuthorName,
			RegistrationDate = detail.RegistrationDate,
			LocalDirectoryName = localDirectoryName,
			LocalDirectoryPath = Path.Combine(packageDirectory, localDirectoryName),
			Stickers = [.. detail.Images.Select((sticker, index) => new Sticker
			{
				Path = sticker.Url,
				Extension = sticker.Extension,
				SortNumber = index,
				ImageUrl = sticker.Url,
				FileName = InvenStickerFileNameHelper.GetStickerFileName(sticker, index),
			})],
			Tags = [.. detail.Tags],
		};

		lock (s_lock)
		{
			s_data.DownloadedPackages.Add(stickerPackage);
		}

		await SaveAsync();

		PackagesChanged?.Invoke();
		WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(ContentSource.Inven, packageIdentifier));
	}

	/// <summary>
	/// 원격 패키지에 로컬에 없는 새 스티커가 몇 개 있는지 확인합니다.
	/// </summary>
	public static async Task<int> GetAdditionalStickerCountAsync(
		ContentSource source,
		string packageIdentifier,
		CancellationToken cancellationToken = default)
	{
		var package = GetDownloadedPackage(source, packageIdentifier)
			?? throw new InvalidOperationException("다운로드된 패키지를 찾을 수 없습니다.");
		var existingStickerPaths = GetExistingStickerPaths(package);

		if (source == ContentSource.Dccon)
		{
			var packageIndex = int.Parse(packageIdentifier, CultureInfo.InvariantCulture);
			var detail = await s_dcconClient.GetPackageDetailAsync(packageIndex, cancellationToken);
			return detail.Stickers.Count(sticker => !existingStickerPaths.Contains(sticker.Path));
		}
		else if (source == ContentSource.Arcacon)
		{
			var packageIndex = int.Parse(packageIdentifier, CultureInfo.InvariantCulture);
			var detail = await App.ArcaconClient.GetPackageDetailAsync(packageIndex, cancellationToken);
			return detail.Stickers.Count(sticker => !existingStickerPaths.Contains(sticker.ImageUrl));
		}
		else if (source == ContentSource.Inven)
		{
			var packageIdentifierAsInteger = int.Parse(packageIdentifier, CultureInfo.InvariantCulture);
			var detail = await s_stickerClient.GetDetailAsync(packageIdentifierAsInteger, cancellationToken);
			return detail.Images.Count(sticker => !existingStickerPaths.Contains(sticker.Url));
		}

		return 0;
	}

	/// <summary>
	/// 다운로드된 패키지에 원격으로 추가된 스티커를 내려받고 로컬 메타데이터를 갱신합니다.
	/// </summary>
	public static async Task<int> SynchronizeDownloadedPackageAsync(
		ContentSource source,
		string packageIdentifier,
		IProgress<(int Completed, int Total)> progress = null,
		CancellationToken cancellationToken = default)
	{
		var package = GetDownloadedPackage(source, packageIdentifier)
			?? throw new InvalidOperationException("다운로드된 패키지를 찾을 수 없습니다.");

		var synchronizedStickerCount = source switch
		{
			ContentSource.Dccon => await SynchronizeDcconPackageAsync(packageIdentifier, package, progress, cancellationToken),
			ContentSource.Arcacon => await SynchronizeArcaconPackageAsync(packageIdentifier, package, progress, cancellationToken),
			ContentSource.Inven => await SynchronizeInvenStickerPackageAsync(packageIdentifier, package, progress, cancellationToken),
			_ => 0
		};

		if (synchronizedStickerCount == 0) return 0;

		await SaveAsync();

		PackagesChanged?.Invoke();
		WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(source, packageIdentifier));
		return synchronizedStickerCount;
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
	/// 다운로드된 특정 패키지를 반환합니다. 없으면 null을 반환합니다.
	/// </summary>
	public static StickerPackage GetDownloadedPackage(ContentSource source, string packageIdentifier)
	{
		lock (s_lock)
		{
			return s_data.DownloadedPackages.FirstOrDefault(
				package => package.Source == source && package.PackageIdentifier == packageIdentifier);
		}
	}

	public static Task<AnimatedPngToWebpPackageConversionResult> ConvertAnimatedPngStickersToWebpAsync(
		IProgress<AnimatedPngToWebpPackageConversionProgress> progress = null,
		CancellationToken cancellationToken = default)
		=> ConvertAnimatedPngStickersToWebpAsync(null, progress, cancellationToken);

	public static async Task<AnimatedPngToWebpPackageConversionResult> ConvertAnimatedPngStickersToWebpAsync(
		IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> selectedPackageKeys,
		IProgress<AnimatedPngToWebpPackageConversionProgress> progress = null,
		CancellationToken cancellationToken = default)
	{
		var conversionCandidates = new List<AnimatedPngStickerConversionCandidate>();
		var convertedPackageKeys = new HashSet<(ContentSource Source, string PackageIdentifier)>();
		var alreadyConvertedCount = 0;
		var notTargetCount = 0;
		var failedCount = 0;
		List<AnimatedPngStickerConversionCandidate> stickerCandidates;

		lock (s_lock)
		{
			var selectedPackageKeySet = selectedPackageKeys is null ? null : selectedPackageKeys.ToHashSet();
			stickerCandidates = s_data.DownloadedPackages
				.Where(package => selectedPackageKeySet is null || selectedPackageKeySet.Contains((package.Source, package.PackageIdentifier)))
				.SelectMany(package => package.Stickers.Select(sticker => CreateAnimatedPngStickerConversionCandidate(package, sticker)))
				.ToList();
		}

		for (var stickerCandidateIndex = 0; stickerCandidateIndex < stickerCandidates.Count; stickerCandidateIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var stickerCandidate = stickerCandidates[stickerCandidateIndex];
			progress?.Report(new AnimatedPngToWebpPackageConversionProgress(
				AnimatedPngToWebpPackageConversionStage.Searching,
				stickerCandidateIndex + 1,
				stickerCandidates.Count,
				stickerCandidate.Package.Title,
				stickerCandidate.Sticker.FileName));

			if (!Path.GetExtension(stickerCandidate.Sticker.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
			{
				notTargetCount++;
				continue;
			}

			if (!File.Exists(stickerCandidate.SourceFilePath))
			{
				failedCount++;
				App.LogException(
					"AnimatedPngToWebpConversion",
					new FileNotFoundException("스티커 원본 파일을 찾을 수 없습니다.", stickerCandidate.SourceFilePath));
				continue;
			}

			if (!string.IsNullOrWhiteSpace(stickerCandidate.Sticker.WebpFileName) && File.Exists(stickerCandidate.WebpFilePath))
			{
				alreadyConvertedCount++;
				continue;
			}

			if (!AnimatedPngToWebpConversionManager.IsAnimatedPng(stickerCandidate.SourceFilePath))
			{
				notTargetCount++;
				continue;
			}

			conversionCandidates.Add(stickerCandidate);
		}

		var convertedCount = 0;
		for (var conversionCandidateIndex = 0; conversionCandidateIndex < conversionCandidates.Count; conversionCandidateIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var conversionCandidate = conversionCandidates[conversionCandidateIndex];
			progress?.Report(new AnimatedPngToWebpPackageConversionProgress(
				AnimatedPngToWebpPackageConversionStage.Converting,
				conversionCandidateIndex,
				conversionCandidates.Count,
				conversionCandidate.Package.Title,
				conversionCandidate.Sticker.FileName));

			try
			{
				AnimatedPngToWebpConversionManager.ConvertAnimatedPngToWebp(
					conversionCandidate.SourceFilePath,
					conversionCandidate.WebpFilePath,
					cancellationToken);

				lock (s_lock) conversionCandidate.Sticker.WebpFileName = conversionCandidate.WebpFileName;
				convertedPackageKeys.Add((conversionCandidate.Package.Source, conversionCandidate.Package.PackageIdentifier));
				convertedCount++;
			}
			catch (Exception exception)
			{
				failedCount++;
				App.LogException("AnimatedPngToWebpConversion", exception);
			}

			progress?.Report(new AnimatedPngToWebpPackageConversionProgress(
				AnimatedPngToWebpPackageConversionStage.Converting,
				conversionCandidateIndex + 1,
				conversionCandidates.Count,
				conversionCandidate.Package.Title,
				conversionCandidate.Sticker.FileName));
		}

		if (convertedCount > 0)
		{
			progress?.Report(new AnimatedPngToWebpPackageConversionProgress(
				AnimatedPngToWebpPackageConversionStage.Saving,
				0,
				1,
				string.Empty,
				string.Empty));

			await SaveAsync();

			PackagesChanged?.Invoke();
			foreach (var packageKey in convertedPackageKeys) WeakReferenceMessenger.Default.Send(new FavoritesOrPackagesChangedMessage(packageKey.Source, packageKey.PackageIdentifier));

			progress?.Report(new AnimatedPngToWebpPackageConversionProgress(
				AnimatedPngToWebpPackageConversionStage.Saving,
				1,
				1,
				string.Empty,
				string.Empty));
		}

		return new AnimatedPngToWebpPackageConversionResult(
			stickerCandidates.Count,
			convertedCount,
			alreadyConvertedCount,
			notTargetCount,
			failedCount);
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
	/// 특정 패키지들에 대한 즐겨찾기가 존재하는지 확인합니다. 패키지 키를 지정하지 않으면 전체 즐겨찾기를 확인합니다.
	/// </summary>
	public static bool HasFavorites(IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> packageKeys = null)
	{
		lock (s_lock)
		{
			if (packageKeys is null) return s_data.Favorites.Count > 0;

			var keySet = packageKeys as HashSet<(ContentSource, string)> ?? [.. packageKeys];
			return s_data.Favorites.Any(
				favorite => keySet.Contains((favorite.Source, favorite.PackageIdentifier)));
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
				if (packagesByKey.Remove(key, out var package)) reordered.Add(package);
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

		HistoryManager.RemoveByPackage(source, packageIdentifier);

		var packageDirectory = GetPackageDirectory(source, packageIdentifier);
		if (Directory.Exists(packageDirectory)) Directory.Delete(packageDirectory, recursive: true);

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

	public static string GetStickerImagePath(ContentSource source, string packageIdentifier, string localDirectoryName, Sticker sticker)
	{
		if (SettingsManager.UseAnimatedPngWebpConversionEnabled && !string.IsNullOrWhiteSpace(sticker.WebpFileName))
		{
			var webpImagePath = GetStickerImagePath(source, packageIdentifier, localDirectoryName, sticker.WebpFileName);
			if (File.Exists(webpImagePath)) return webpImagePath;
		}

		return GetStickerImagePath(source, packageIdentifier, localDirectoryName, sticker.FileName);
	}

    /// <summary>
    /// 패키지의 메인 이미지 로컬 파일 경로를 반환합니다.
    /// </summary>
    public static string GetMainImagePath(ContentSource source, string packageIdentifier, string mainImageFileName)
		=> Path.Combine(GetPackageDirectory(source, packageIdentifier), mainImageFileName);

    private static string GetPackageDirectory(ContentSource source, string packageIdentifier)
		=> Path.Combine(s_basePath, source.ToString(), packageIdentifier);

	private static AnimatedPngStickerConversionCandidate CreateAnimatedPngStickerConversionCandidate(StickerPackage package, Sticker sticker)
	{
		var webpFileName = $"{Path.GetFileNameWithoutExtension(sticker.FileName)}.webp";
		var sourceFilePath = GetStickerImagePath(package.Source, package.PackageIdentifier, package.LocalDirectoryName, sticker.FileName);
		var webpFilePath = GetStickerImagePath(package.Source, package.PackageIdentifier, package.LocalDirectoryName, webpFileName);
		return new AnimatedPngStickerConversionCandidate(package, sticker, sourceFilePath, webpFileName, webpFilePath);
	}

	private static async Task<int> SynchronizeDcconPackageAsync(
		string packageIdentifier,
		StickerPackage package,
		IProgress<(int Completed, int Total)> progress,
		CancellationToken cancellationToken)
	{
		var packageIndex = int.Parse(packageIdentifier, CultureInfo.InvariantCulture);
		var detail = await s_dcconClient.GetPackageDetailAsync(packageIndex, cancellationToken);
		var existingStickerPaths = GetExistingStickerPaths(package);
		var additionalStickers = detail.Stickers.Where(sticker => !existingStickerPaths.Contains(sticker.Path)).ToList();
		if (additionalStickers.Count == 0) return 0;

		var stickerDirectory = EnsurePackageStickerDirectory(ContentSource.Dccon, packageIdentifier, package);
		var synchronizedStickerCount = 0;

		foreach (var sticker in additionalStickers)
		{
			var fileName = DcconFileNameHelper.GetStickerFileName(sticker);
			var filePath = Path.Combine(stickerDirectory, fileName);
			var imageData = await s_dcconClient.DownloadStickerAsync(sticker, cancellationToken);
			await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);

			synchronizedStickerCount++;
			progress?.Report((synchronizedStickerCount, additionalStickers.Count));
		}

		lock (s_lock)
		{
			ApplyDcconPackageMetadata(package, detail);
			AddSynchronizedStickers(package, additionalStickers.Select(CreateSticker));
		}

		return synchronizedStickerCount;
	}

	private static async Task<int> SynchronizeArcaconPackageAsync(
		string packageIdentifier,
		StickerPackage package,
		IProgress<(int Completed, int Total)> progress,
		CancellationToken cancellationToken)
	{
		var packageIndex = int.Parse(packageIdentifier, CultureInfo.InvariantCulture);
		var detail = await App.ArcaconClient.GetPackageDetailAsync(packageIndex, cancellationToken);
		var existingStickerPaths = GetExistingStickerPaths(package);
		var additionalStickers = detail.Stickers.Where(sticker => !existingStickerPaths.Contains(sticker.ImageUrl)).ToList();
		if (additionalStickers.Count == 0) return 0;

		var stickerDirectory = EnsurePackageStickerDirectory(ContentSource.Arcacon, packageIdentifier, package);
		var synchronizedStickerCount = 0;
		var synchronizedStickers = new List<Sticker>();

		foreach (var sticker in additionalStickers)
		{
			var imageData = await App.ArcaconClient.DownloadStickerAsync(sticker, cancellationToken);
			var fileName = ArcaconFileNameHelper.GetStickerFileName(sticker, imageData);
			var filePath = Path.Combine(stickerDirectory, fileName);
			await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);
			synchronizedStickers.Add(CreateSticker(sticker, fileName));

			synchronizedStickerCount++;
			progress?.Report((synchronizedStickerCount, additionalStickers.Count));
		}

		lock (s_lock)
		{
			ApplyArcaconPackageMetadata(package, detail, packageIndex);
			AddSynchronizedStickers(package, synchronizedStickers);
		}

		return synchronizedStickerCount;
	}

	private static async Task<int> SynchronizeInvenStickerPackageAsync(
		string packageIdentifier,
		StickerPackage package,
		IProgress<(int Completed, int Total)> progress,
		CancellationToken cancellationToken)
	{
		var packageIdentifierAsInteger = int.Parse(packageIdentifier, CultureInfo.InvariantCulture);
		var detail = await s_stickerClient.GetDetailAsync(packageIdentifierAsInteger, cancellationToken);
		var existingStickerPaths = GetExistingStickerPaths(package);
		var additionalStickers = detail.Images
			.Select((sticker, index) => (Sticker: sticker, Index: index))
			.Where(stickerContext => !existingStickerPaths.Contains(stickerContext.Sticker.Url))
			.ToList();
		if (additionalStickers.Count == 0) return 0;

		var stickerDirectory = EnsurePackageStickerDirectory(ContentSource.Inven, packageIdentifier, package);
		var synchronizedStickerCount = 0;

		foreach (var stickerContext in additionalStickers)
		{
			var fileName = InvenStickerFileNameHelper.GetStickerFileName(stickerContext.Sticker, stickerContext.Index);
			var filePath = Path.Combine(stickerDirectory, fileName);
			var imageData = await s_stickerClient.DownloadImageAsync(stickerContext.Sticker, cancellationToken);
			await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);

			synchronizedStickerCount++;
			progress?.Report((synchronizedStickerCount, additionalStickers.Count));
		}

		lock (s_lock)
		{
			ApplyInvenStickerPackageMetadata(package, detail);
			AddSynchronizedStickers(package, additionalStickers.Select(stickerContext => CreateSticker(stickerContext.Sticker, stickerContext.Index)));
		}

		return synchronizedStickerCount;
	}

	private static HashSet<string> GetExistingStickerPaths(StickerPackage package)
		=> package.Stickers.Select(sticker => sticker.Path).ToHashSet(StringComparer.Ordinal);

	private static string EnsurePackageStickerDirectory(ContentSource source, string packageIdentifier, StickerPackage package)
	{
		var packageDirectory = GetPackageDirectory(source, packageIdentifier);
		if (string.IsNullOrWhiteSpace(package.LocalDirectoryName)) package.LocalDirectoryName = GetSafeLocalDirectoryName(source, packageIdentifier, package.Title);

		var stickerDirectory = Path.Combine(packageDirectory, package.LocalDirectoryName);
		Directory.CreateDirectory(stickerDirectory);
		package.LocalDirectoryPath = stickerDirectory;
		return stickerDirectory;
	}

	private static string GetSafeLocalDirectoryName(ContentSource source, string packageIdentifier, string title)
	{
		var safeDirectoryName = source switch
		{
			ContentSource.Dccon => DcconFileNameHelper.SanitizeFileName(title),
			ContentSource.Arcacon => ArcaconFileNameHelper.SanitizeFileName(title),
			ContentSource.Inven => InvenStickerFileNameHelper.SanitizeFileName(title),
			_ => title
		};

		return string.IsNullOrWhiteSpace(safeDirectoryName) ? packageIdentifier : safeDirectoryName;
	}

	private static void AddSynchronizedStickers(StickerPackage package, IEnumerable<Sticker> stickers)
	{
		package.Stickers.AddRange(stickers);
		package.Stickers = [.. package.Stickers.OrderBy(sticker => sticker.SortNumber)];
	}

	private static async Task<List<Sticker>> DownloadArcaconStickerFilesAsync(
		IReadOnlyList<ArcaconSticker> stickers,
		string stickerDirectory,
		IProgress<(int Completed, int Total)> progress,
		CancellationToken cancellationToken)
	{
		var downloadedStickers = new Sticker[stickers.Count];
		var downloadedStickerCount = 0;
		using var semaphore = new SemaphoreSlim(4);
		var tasks = stickers.Select(async (sticker, stickerIndex) =>
		{
			await semaphore.WaitAsync(cancellationToken);
			try
			{
				var imageData = await App.ArcaconClient.DownloadStickerAsync(sticker, cancellationToken);
				var fileName = ArcaconFileNameHelper.GetStickerFileName(sticker, imageData);
				var filePath = Path.Combine(stickerDirectory, fileName);
				await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);
				downloadedStickers[stickerIndex] = CreateSticker(sticker, fileName);

				var completedCount = Interlocked.Increment(ref downloadedStickerCount);
				progress?.Report((completedCount, stickers.Count));
			}
			finally { semaphore.Release(); }
		}).ToList();

		await Task.WhenAll(tasks);
		return [.. downloadedStickers];
	}

	private static Sticker CreateSticker(DcconSticker sticker) => new()
	{
		Path = sticker.Path,
		Title = sticker.Title,
		Extension = sticker.Extension,
		SortNumber = sticker.SortNumber,
		ImageUrl = sticker.ImageUrl,
		FileName = DcconFileNameHelper.GetStickerFileName(sticker),
	};

	private static Sticker CreateSticker(ArcaconSticker sticker, string fileName) => new()
	{
		Path = sticker.ImageUrl,
		Title = string.IsNullOrWhiteSpace(sticker.Title) ? $"스티커 {sticker.SortNumber}" : sticker.Title,
		Extension = Path.GetExtension(fileName).TrimStart('.'),
		SortNumber = sticker.SortNumber,
		ImageUrl = sticker.ImageUrl,
		FileName = fileName,
	};

	private static Sticker CreateSticker(InvenStickerImage sticker, int index) => new()
	{
		Path = sticker.Url,
		Extension = sticker.Extension,
		SortNumber = index,
		ImageUrl = sticker.Url,
		FileName = InvenStickerFileNameHelper.GetStickerFileName(sticker, index),
	};

	private static void ApplyDcconPackageMetadata(StickerPackage package, DcconPackageDetail detail)
	{
		package.Title = detail.Title;
		package.Description = detail.Description;
		package.MainImagePath = detail.MainImagePath;
		package.SellerName = detail.SellerName;
		package.RegistrationDate = detail.RegistrationDate;
		package.Tags = [.. detail.Tags];
	}

	private static void ApplyArcaconPackageMetadata(StickerPackage package, ArcaconPackageDetail detail, int packageIndex)
	{
		package.Title = detail.Title;
		package.Description = detail.Price == 0 ? string.Empty : $"{detail.Price}pt";
		package.MainImagePath = $"https://arca.live/api/emoticon/{packageIndex}/thumb";
		package.SellerName = detail.SellerName;
		package.RegistrationDate = detail.RegistrationDate;
		package.Tags = [.. detail.Tags];
	}

	private static void ApplyInvenStickerPackageMetadata(StickerPackage package, InvenStickerPackageDetail detail)
	{
		package.Title = detail.Title;
		package.Description = detail.PriceInfo;
		package.MainImagePath = detail.ThumbnailUrl;
		package.SellerName = detail.AuthorName;
		package.RegistrationDate = detail.RegistrationDate;
		package.Tags = [.. detail.Tags];
	}

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
		if (source == ContentSource.Dccon) request.Headers.Add("Referer", "https://dccon.dcinside.com");
		else if (source == ContentSource.Arcacon) request.Headers.Add("Referer", "https://arca.live");

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
	public static Task ExportAsync(
		string destinationFilePath,
		bool exportFavorites = true,
		IProgress<PackageArchiveProgress> progress = null,
		CancellationToken cancellationToken = default)
		=> ExportAsync(destinationFilePath, selectedPackageKeys: null, exportFavorites, progress, cancellationToken);

	/// <summary>
	/// 선택한 패키지와 관련 즐겨찾기를 .yip 파일로 내보냅니다.
	/// </summary>
	public static async Task ExportAsync(
		string destinationFilePath,
		IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> selectedPackageKeys,
		bool exportFavorites = true,
		IProgress<PackageArchiveProgress> progress = null,
		CancellationToken cancellationToken = default)
	{
		if (File.Exists(destinationFilePath)) File.Delete(destinationFilePath);

		string dataJson;
		List<StickerPackage> packages;
		HashSet<(ContentSource Source, string PackageIdentifier)> selectedPackageKeySet = null;
		lock (s_lock)
		{
			if (selectedPackageKeys is not null) selectedPackageKeySet = [.. selectedPackageKeys];

			var exportData = new ContentsManagerData
			{
				DownloadedPackages = selectedPackageKeySet is null
					? [.. s_data.DownloadedPackages]
					: [.. s_data.DownloadedPackages.Where(package => selectedPackageKeySet.Contains((package.Source, package.PackageIdentifier)))],
				Favorites = exportFavorites
					? selectedPackageKeySet is null
						? [.. s_data.Favorites]
						: [.. s_data.Favorites.Where(favorite => selectedPackageKeySet.Contains((favorite.Source, favorite.PackageIdentifier)))]
					: [],
			};

			dataJson = JsonSerializer.Serialize(exportData, ContentsManagerJsonContext.Default.ContentsManagerData);
			packages = exportData.DownloadedPackages;
		}

		var contentsJsonBytes = Encoding.UTF8.GetBytes(dataJson);
		var packageArchiveFileEntries = CreatePackageArchiveFileEntries(packages);
		var totalFileCount = packageArchiveFileEntries.Count + 1;
		var totalByteCount = packageArchiveFileEntries.Sum(packageArchiveFileEntry => packageArchiveFileEntry.Length) + contentsJsonBytes.LongLength;
		ReportPackageArchiveProgress(
			progress,
			PackageArchiveProgressStage.Preparing,
			0,
			totalFileCount,
			0,
			totalByteCount,
			0,
			0,
			string.Empty);

		await using var destinationFileStream = new FileStream(
			destinationFilePath,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			StreamCopyBufferSize,
			true);
		using var exportArchive = new ZipArchive(destinationFileStream, ZipArchiveMode.Create);

		var completedFileCount = 0;
		var completedByteCount = 0L;
		completedByteCount = await WriteArchiveEntryAsync(
			exportArchive,
			"contents.json",
			contentsJsonBytes,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			progress,
			cancellationToken);
		completedFileCount++;
		ReportPackageArchiveProgress(
			progress,
			PackageArchiveProgressStage.AddingToArchive,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			0,
			0,
			string.Empty);

		foreach (var packageArchiveFileEntry in packageArchiveFileEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			completedByteCount = await WriteArchiveFileEntryAsync(
				exportArchive,
				packageArchiveFileEntry,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				progress,
				cancellationToken);
			completedFileCount++;
			ReportPackageArchiveProgress(
				progress,
				PackageArchiveProgressStage.AddingToArchive,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				0,
				0,
				string.Empty);
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
		var importedData = await ReadDataFromImportFileAsync(sourceFilePath, cancellationToken);
		return importedData.DownloadedPackages;
	}

	/// <summary>
	/// .yip 파일에 즐겨찾기 데이터가 포함되어 있는지 확인합니다.
	/// </summary>
	/// <param name="sourceFilePath">불러올 .yip 파일 경로</param>
	/// <param name="cancellationToken">취소 토큰</param>
	public static async Task<bool> HasFavoritesInImportFileAsync(
		string sourceFilePath,
		CancellationToken cancellationToken = default)
	{
		var importedData = await ReadDataFromImportFileAsync(sourceFilePath, cancellationToken);
		return importedData.Favorites.Count > 0;
	}

	/// <summary>
	/// .yip 파일에서 특정 패키지들에 대한 즐겨찾기가 포함되어 있는지 확인합니다.
	/// </summary>
	public static async Task<bool> HasFavoritesForPackagesInImportFileAsync(
		string sourceFilePath,
		IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> packageKeys,
		CancellationToken cancellationToken = default)
	{
		var importedData = await ReadDataFromImportFileAsync(sourceFilePath, cancellationToken);
		var keySet = packageKeys as HashSet<(ContentSource, string)> ?? [.. packageKeys];
		return importedData.Favorites.Any(
			favorite => keySet.Contains((favorite.Source, favorite.PackageIdentifier)));
	}

	/// <summary>
	/// .yip 파일에서 패키지의 대표 이미지만 임시 디렉토리에 추출합니다.
	/// </summary>
	public static void ExtractMainImagesFromImportFile(
		string sourceFilePath,
		IReadOnlyList<StickerPackage> packages,
		string targetDirectory)
	{
		using var archive = ZipFile.OpenRead(sourceFilePath);
		foreach (var package in packages)
		{
			if (string.IsNullOrWhiteSpace(package.MainImageFileName)) continue;

			var entryPath = $"{package.Source}/{package.PackageIdentifier}/{package.MainImageFileName}";
			var entry = archive.GetEntry(entryPath);
			if (entry is null) continue;

			var packageDirectory = Path.Combine(targetDirectory, package.Source.ToString(), package.PackageIdentifier);
			Directory.CreateDirectory(packageDirectory);
			entry.ExtractToFile(Path.Combine(packageDirectory, package.MainImageFileName));
		}
	}

	/// <summary>
	/// .yip 파일에 포함된 전체 데이터를 읽어옵니다.
	/// </summary>
	private static async Task<ContentsManagerData> ReadDataFromImportFileAsync(
		string sourceFilePath,
		CancellationToken cancellationToken = default)
	{
		using var importArchive = ZipFile.OpenRead(sourceFilePath);
		return await ReadDataFromImportArchiveAsync(importArchive, cancellationToken);
	}

	private static async Task<ContentsManagerData> ReadDataFromImportArchiveAsync(
		ZipArchive importArchive,
		CancellationToken cancellationToken = default)
	{
		var importedDataEntry = importArchive.GetEntry("contents.json")
			?? throw new InvalidOperationException("유효하지 않은 .yip 파일입니다. contents.json이 존재하지 않습니다.");

		using var importedDataStream = importedDataEntry.Open();
		var importedData = await DeserializeImportedDataAsync(importedDataStream, cancellationToken);
		MigrateImportedPackageIdentifiers(importedData);
		return importedData;
	}

	/// <summary>
	/// .yip 파일을 불러옵니다.
	/// </summary>
	/// <param name="sourceFilePath">불러올 .yip 파일 경로</param>
	/// <param name="replaceAll">true이면 기존 데이터를 모두 삭제하고 새로 시작, false이면 기존 데이터에 추가만 합니다.</param>
	/// <param name="importFavorites">true이면 즐겨찾기도 함께 불러옵니다.</param>
	/// <param name="selectedPackageKeys">null이 아니면 선택한 패키지만 불러옵니다.</param>
	/// <param name="cancellationToken">취소 토큰</param>
	public static async Task ImportAsync(
		string sourceFilePath,
		bool replaceAll,
		bool importFavorites = true,
		IReadOnlyCollection<(ContentSource Source, string PackageIdentifier)> selectedPackageKeys = null,
		IProgress<PackageArchiveProgress> progress = null,
		CancellationToken cancellationToken = default)
	{
		var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"YellowInside_Import_{Guid.NewGuid():N}");
		try
		{
			ReportPackageArchiveProgress(
				progress,
				PackageArchiveProgressStage.Preparing,
				0,
				0,
				0,
				0,
				0,
				0,
				string.Empty);

			using var importArchive = ZipFile.OpenRead(sourceFilePath);
			var importedData = await ReadDataFromImportArchiveAsync(importArchive, cancellationToken);

			HashSet<(ContentSource Source, string PackageIdentifier)> selectedPackageKeySet = null;
			if (selectedPackageKeys is not null)
			{
				selectedPackageKeySet = [.. selectedPackageKeys];
				importedData.DownloadedPackages = [.. importedData.DownloadedPackages.Where(package => selectedPackageKeySet.Contains((package.Source, package.PackageIdentifier)))];
				importedData.Favorites = [.. importedData.Favorites.Where(favorite => selectedPackageKeySet.Contains((favorite.Source, favorite.PackageIdentifier)))];
			}

			var importedPackageKeySet = importedData.DownloadedPackages
				.Select(package => (package.Source, package.PackageIdentifier))
				.ToHashSet();

			Directory.CreateDirectory(temporaryDirectory);
			await ExtractPackageArchiveEntriesAsync(
				importArchive,
				temporaryDirectory,
				importedPackageKeySet,
				progress,
				cancellationToken);

			if (replaceAll)
			{
				lock (s_lock)
				{
					foreach (var package in s_data.DownloadedPackages)
					{
						var directory = GetPackageDirectory(package.Source, package.PackageIdentifier);
						if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
					}

					if (importFavorites) s_data = importedData;
					else
					{
						s_data = importedData;
						s_data.Favorites = [];
					}
				}
			}
			else
			{
				lock (s_lock)
				{
					// For partial import, remove existing selected packages to allow overwrite
					if (selectedPackageKeySet is not null)
					{
						s_data.DownloadedPackages = [.. s_data.DownloadedPackages
							.Where(package => !selectedPackageKeySet.Contains((package.Source, package.PackageIdentifier)))];
						if (importFavorites)
						{
							s_data.Favorites = [.. s_data.Favorites
								.Where(favorite => !selectedPackageKeySet.Contains((favorite.Source, favorite.PackageIdentifier)))];
						}
					}

					var existingKeys = s_data.DownloadedPackages.Select(package => (package.Source, package.PackageIdentifier)).ToHashSet();

					foreach (var package in importedData.DownloadedPackages)
					{
						if (existingKeys.Contains((package.Source, package.PackageIdentifier))) continue;
						s_data.DownloadedPackages.Add(package);
					}

					if (importFavorites)
					{
						var existingFavoriteKeys = s_data.Favorites
							.Select(favorite => (favorite.Source, favorite.PackageIdentifier, favorite.StickerPath))
							.ToHashSet();

						foreach (var favorite in importedData.Favorites)
						{
							if (existingFavoriteKeys.Contains((favorite.Source, favorite.PackageIdentifier, favorite.StickerPath))) continue;
							s_data.Favorites.Add(favorite);
						}
					}
				}

				if (selectedPackageKeySet is not null)
				{
					foreach (var package in importedData.DownloadedPackages)
					{
						var targetDirectory = GetPackageDirectory(package.Source, package.PackageIdentifier);
						if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, recursive: true);
					}
				}
			}

			var shouldOverwriteExistingTargetDirectories = replaceAll || selectedPackageKeySet is not null;
			var packageArchiveFileCopyEntries = CreateImportedPackageFileCopyEntries(
				temporaryDirectory,
				importedData.DownloadedPackages,
				shouldOverwriteExistingTargetDirectories);
			await CopyPackageArchiveFileEntriesAsync(
				packageArchiveFileCopyEntries,
				PackageArchiveProgressStage.ApplyingPackageFiles,
				progress,
				cancellationToken);

			ReportPackageArchiveProgress(
				progress,
				PackageArchiveProgressStage.Saving,
				0,
				0,
				0,
				0,
				0,
				0,
				string.Empty);
			await SaveAsync();

			ReportPackageArchiveProgress(
				progress,
				PackageArchiveProgressStage.Refreshing,
				0,
				0,
				0,
				0,
				0,
				0,
				string.Empty);
			PackagesChanged?.Invoke();
			if (importFavorites) FavoritesChanged?.Invoke();

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
			if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
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

	private static List<PackageArchiveFileEntry> CreatePackageArchiveFileEntries(IEnumerable<StickerPackage> packages)
	{
		var packageArchiveFileEntries = new List<PackageArchiveFileEntry>();
		foreach (var package in packages)
		{
			var sourceDirectory = GetPackageDirectory(package.Source, package.PackageIdentifier);
			if (!Directory.Exists(sourceDirectory)) continue;

			var packageArchiveDirectory = NormalizeArchiveEntryName(Path.Combine(package.Source.ToString(), package.PackageIdentifier));
			foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
			{
				var relativeFilePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
				var relativeArchivePath = NormalizeArchiveEntryName(Path.Combine(packageArchiveDirectory, relativeFilePath));
				var sourceFileInfo = new FileInfo(sourceFilePath);
				packageArchiveFileEntries.Add(new PackageArchiveFileEntry(sourceFilePath, relativeArchivePath, sourceFileInfo.Length));
			}
		}

		return packageArchiveFileEntries;
	}

	private static async Task<long> WriteArchiveFileEntryAsync(
		ZipArchive exportArchive,
		PackageArchiveFileEntry packageArchiveFileEntry,
		int completedFileCount,
		int totalFileCount,
		long completedByteCount,
		long totalByteCount,
		IProgress<PackageArchiveProgress> progress,
		CancellationToken cancellationToken)
	{
		await using var sourceFileStream = new FileStream(
			packageArchiveFileEntry.SourceFilePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			StreamCopyBufferSize,
			true);
		return await WriteArchiveEntryAsync(
			exportArchive,
			packageArchiveFileEntry.RelativePath,
			sourceFileStream,
			packageArchiveFileEntry.Length,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			progress,
			cancellationToken);
	}

	private static async Task<long> WriteArchiveEntryAsync(
		ZipArchive exportArchive,
		string relativePath,
		byte[] contents,
		int completedFileCount,
		int totalFileCount,
		long completedByteCount,
		long totalByteCount,
		IProgress<PackageArchiveProgress> progress,
		CancellationToken cancellationToken)
	{
		using var sourceStream = new MemoryStream(contents, writable: false);
		return await WriteArchiveEntryAsync(
			exportArchive,
			relativePath,
			sourceStream,
			contents.LongLength,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			progress,
			cancellationToken);
	}

	private static async Task<long> WriteArchiveEntryAsync(
		ZipArchive exportArchive,
		string relativePath,
		Stream sourceStream,
		long entryLength,
		int completedFileCount,
		int totalFileCount,
		long completedByteCount,
		long totalByteCount,
		IProgress<PackageArchiveProgress> progress,
		CancellationToken cancellationToken)
	{
		var archiveEntry = exportArchive.CreateEntry(NormalizeArchiveEntryName(relativePath), CompressionLevel.Optimal);
		await using var archiveEntryStream = archiveEntry.Open();
		return await CopyStreamWithPackageArchiveProgressAsync(
			sourceStream,
			archiveEntryStream,
			PackageArchiveProgressStage.AddingToArchive,
			NormalizeArchiveEntryName(relativePath),
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			entryLength,
			progress,
			cancellationToken);
	}

	private static async Task ExtractPackageArchiveEntriesAsync(
		ZipArchive importArchive,
		string temporaryDirectory,
		HashSet<(ContentSource Source, string PackageIdentifier)> packageKeySet,
		IProgress<PackageArchiveProgress> progress,
		CancellationToken cancellationToken)
	{
		var packageArchivePrefixes = packageKeySet
			.Select(packageKey => $"{packageKey.Source}/{packageKey.PackageIdentifier}/")
			.ToHashSet(StringComparer.Ordinal);
		var archiveEntries = importArchive.Entries
			.Where(archiveEntry => IsPackageArchiveFileEntryIncluded(archiveEntry, packageArchivePrefixes))
			.ToList();
		var totalFileCount = archiveEntries.Count;
		var totalByteCount = archiveEntries.Sum(archiveEntry => archiveEntry.Length);
		var destinationRootDirectoryPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
		var completedFileCount = 0;
		var completedByteCount = 0L;

		ReportPackageArchiveProgress(
			progress,
			PackageArchiveProgressStage.ExtractingArchive,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			0,
			0,
			string.Empty);

		foreach (var archiveEntry in archiveEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var relativeArchivePath = NormalizeArchiveEntryName(archiveEntry.FullName);
			var destinationFilePath = Path.GetFullPath(Path.Combine(temporaryDirectory, relativeArchivePath));
			if (!destinationFilePath.StartsWith(destinationRootDirectoryPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("압축 파일에 잘못된 상대 경로가 포함되어 있습니다.");

			var destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
			if (!string.IsNullOrEmpty(destinationDirectoryPath)) Directory.CreateDirectory(destinationDirectoryPath);

			await using var archiveEntryStream = archiveEntry.Open();
			await using var destinationFileStream = new FileStream(
				destinationFilePath,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				StreamCopyBufferSize,
				true);
			completedByteCount = await CopyStreamWithPackageArchiveProgressAsync(
				archiveEntryStream,
				destinationFileStream,
				PackageArchiveProgressStage.ExtractingArchive,
				relativeArchivePath,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				archiveEntry.Length,
				progress,
				cancellationToken);
			completedFileCount++;
			ReportPackageArchiveProgress(
				progress,
				PackageArchiveProgressStage.ExtractingArchive,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				0,
				0,
				string.Empty);
		}
	}

	private static bool IsPackageArchiveFileEntryIncluded(ZipArchiveEntry archiveEntry, HashSet<string> packageArchivePrefixes)
	{
		if (string.IsNullOrEmpty(archiveEntry.Name)) return false;

		var relativeArchivePath = NormalizeArchiveEntryName(archiveEntry.FullName);
		return packageArchivePrefixes.Any(packageArchivePrefix => relativeArchivePath.StartsWith(packageArchivePrefix, StringComparison.Ordinal));
	}

	private static List<PackageArchiveFileCopyEntry> CreateImportedPackageFileCopyEntries(
		string temporaryDirectory,
		IEnumerable<StickerPackage> packages,
		bool shouldOverwriteExistingTargetDirectories)
	{
		var packageArchiveFileCopyEntries = new List<PackageArchiveFileCopyEntry>();
		foreach (var package in packages)
		{
			var sourceDirectory = Path.Combine(temporaryDirectory, package.Source.ToString(), package.PackageIdentifier);
			if (!Directory.Exists(sourceDirectory)) continue;

			var targetDirectory = GetPackageDirectory(package.Source, package.PackageIdentifier);
			if (!shouldOverwriteExistingTargetDirectories && Directory.Exists(targetDirectory)) continue;

			foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
			{
				var relativeFilePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
				var destinationFilePath = Path.Combine(targetDirectory, relativeFilePath);
				var relativeArchivePath = NormalizeArchiveEntryName(Path.Combine(package.Source.ToString(), package.PackageIdentifier, relativeFilePath));
				var sourceFileInfo = new FileInfo(sourceFilePath);
				packageArchiveFileCopyEntries.Add(new PackageArchiveFileCopyEntry(sourceFilePath, destinationFilePath, relativeArchivePath, sourceFileInfo.Length));
			}
		}

		return packageArchiveFileCopyEntries;
	}

	private static async Task CopyPackageArchiveFileEntriesAsync(
		IReadOnlyList<PackageArchiveFileCopyEntry> packageArchiveFileCopyEntries,
		PackageArchiveProgressStage stage,
		IProgress<PackageArchiveProgress> progress,
		CancellationToken cancellationToken)
	{
		var totalFileCount = packageArchiveFileCopyEntries.Count;
		var totalByteCount = packageArchiveFileCopyEntries.Sum(packageArchiveFileCopyEntry => packageArchiveFileCopyEntry.Length);
		var completedFileCount = 0;
		var completedByteCount = 0L;

		ReportPackageArchiveProgress(
			progress,
			stage,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			0,
			0,
			string.Empty);

		foreach (var packageArchiveFileCopyEntry in packageArchiveFileCopyEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var destinationDirectoryPath = Path.GetDirectoryName(packageArchiveFileCopyEntry.DestinationFilePath);
			if (!string.IsNullOrEmpty(destinationDirectoryPath)) Directory.CreateDirectory(destinationDirectoryPath);

			await using var sourceFileStream = new FileStream(
				packageArchiveFileCopyEntry.SourceFilePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				StreamCopyBufferSize,
				true);
			await using var destinationFileStream = new FileStream(
				packageArchiveFileCopyEntry.DestinationFilePath,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				StreamCopyBufferSize,
				true);
			completedByteCount = await CopyStreamWithPackageArchiveProgressAsync(
				sourceFileStream,
				destinationFileStream,
				stage,
				packageArchiveFileCopyEntry.RelativePath,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				packageArchiveFileCopyEntry.Length,
				progress,
				cancellationToken);
			completedFileCount++;
			ReportPackageArchiveProgress(
				progress,
				stage,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				0,
				0,
				string.Empty);
		}
	}

	private static async Task<long> CopyStreamWithPackageArchiveProgressAsync(
		Stream sourceStream,
		Stream destinationStream,
		PackageArchiveProgressStage stage,
		string relativePath,
		int completedFileCount,
		int totalFileCount,
		long completedByteCount,
		long totalByteCount,
		long currentFileTotalByteCount,
		IProgress<PackageArchiveProgress> progress,
		CancellationToken cancellationToken)
	{
		var currentFileCompletedByteCount = 0L;
		var streamCopyBuffer = new byte[StreamCopyBufferSize];
		ReportPackageArchiveProgress(
			progress,
			stage,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			currentFileCompletedByteCount,
			currentFileTotalByteCount,
			relativePath);

		while (true)
		{
			var readByteCount = await sourceStream.ReadAsync(streamCopyBuffer, cancellationToken);
			if (readByteCount == 0) break;

			await destinationStream.WriteAsync(streamCopyBuffer.AsMemory(0, readByteCount), cancellationToken);
			currentFileCompletedByteCount += readByteCount;
			completedByteCount += readByteCount;
			ReportPackageArchiveProgress(
				progress,
				stage,
				completedFileCount,
				totalFileCount,
				completedByteCount,
				totalByteCount,
				currentFileCompletedByteCount,
				currentFileTotalByteCount,
				relativePath);
		}

		return completedByteCount;
	}

	private static void ReportPackageArchiveProgress(
		IProgress<PackageArchiveProgress> progress,
		PackageArchiveProgressStage stage,
		int completedFileCount,
		int totalFileCount,
		long completedByteCount,
		long totalByteCount,
		long currentFileCompletedByteCount,
		long currentFileTotalByteCount,
		string relativePath)
		=> progress?.Report(new PackageArchiveProgress(
			stage,
			completedFileCount,
			totalFileCount,
			completedByteCount,
			totalByteCount,
			currentFileCompletedByteCount,
			currentFileTotalByteCount,
			relativePath));

	private static string NormalizeArchiveEntryName(string path) => path.Replace('\\', '/');

	private static string EnsureTrailingDirectorySeparator(string directoryPath)
		=> Path.EndsInDirectorySeparator(directoryPath) ? directoryPath : directoryPath + Path.DirectorySeparatorChar;

	private sealed record PackageArchiveFileEntry(
		string SourceFilePath,
		string RelativePath,
		long Length);

	private sealed record PackageArchiveFileCopyEntry(
		string SourceFilePath,
		string DestinationFilePath,
		string RelativePath,
		long Length);

	private sealed record AnimatedPngStickerConversionCandidate(
		StickerPackage Package,
		Sticker Sticker,
		string SourceFilePath,
		string WebpFileName,
		string WebpFilePath);

	private static async Task SaveAsync()
	{
		string json;
		lock (s_lock) json = JsonSerializer.Serialize(s_data, ContentsManagerJsonContext.Default.ContentsManagerData);

		var directory = Path.GetDirectoryName(s_dataFilePath);
		if (directory is not null) Directory.CreateDirectory(directory);

		await File.WriteAllTextAsync(s_dataFilePath, json);
	}
}

public enum PackageArchiveProgressStage
{
	Preparing,
	AddingToArchive,
	ExtractingArchive,
	ApplyingPackageFiles,
	Saving,
	Refreshing,
}

public sealed record PackageArchiveProgress(
	PackageArchiveProgressStage Stage,
	int CompletedFileCount,
	int TotalFileCount,
	long CompletedByteCount,
	long TotalByteCount,
	long CurrentFileCompletedByteCount,
	long CurrentFileTotalByteCount,
	string CurrentRelativePath)
{
	public double? ProgressPercentage
		=> TotalByteCount > 0
			? Math.Clamp(CompletedByteCount * 100d / TotalByteCount, 0d, 100d)
			: TotalFileCount > 0
				? Math.Clamp(CompletedFileCount * 100d / TotalFileCount, 0d, 100d)
				: null;

	public double? CurrentFileProgressPercentage
		=> CurrentFileTotalByteCount > 0
			? Math.Clamp(CurrentFileCompletedByteCount * 100d / CurrentFileTotalByteCount, 0d, 100d)
			: null;
}

public enum AnimatedPngToWebpPackageConversionStage
{
	Searching,
	Converting,
	Saving,
}

public sealed record AnimatedPngToWebpPackageConversionProgress(
	AnimatedPngToWebpPackageConversionStage Stage,
	int CompletedCount,
	int TotalCount,
	string PackageTitle,
	string FileName)
{
	public double? ProgressPercentage
		=> TotalCount > 0 ? Math.Clamp(CompletedCount * 100d / TotalCount, 0d, 100d) : null;
}

public sealed record AnimatedPngToWebpPackageConversionResult(
	int TotalStickerCount,
	int ConvertedCount,
	int AlreadyConvertedCount,
	int NotTargetCount,
	int FailedCount);
