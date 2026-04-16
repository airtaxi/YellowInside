namespace YellowInside.Models;

/// <summary>
/// 즐겨찾기에 저장된 스티커 참조
/// </summary>
public class FavoriteSticker
{
	/// <summary>콘텐츠 소스</summary>
	public ContentSource Source { get; set; }

	/// <summary>패키지 고유 식별자 (string 기반)</summary>
	public string PackageIdentifier { get; set; } = string.Empty;

	/// <summary>Legacy integer ID. .yip 임포트 하위 호환을 위해 영구 유지 필요. 제거 금지.</summary>
	[System.Obsolete("Use PackageIdentifier. Do NOT remove — required for .yip backward compatibility.")]
	public int PackageIndex { get; set; }

	/// <summary>스티커 경로 (패키지 내 고유 식별자)</summary>
	public string StickerPath { get; set; } = string.Empty;
}
