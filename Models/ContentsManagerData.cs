using System.Collections.Generic;

namespace YellowInside.Models;

/// <summary>
/// ContentsManager의 영속화 데이터 루트 객체
/// </summary>
public class ContentsManagerData
{
	/// <summary>다운로드된 패키지 목록</summary>
	public List<StickerPackage> DownloadedPackages { get; set; } = [];

	/// <summary>즐겨찾기 스티커 목록</summary>
	public List<FavoriteSticker> Favorites { get; set; } = [];
}
