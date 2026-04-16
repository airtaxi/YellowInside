namespace YellowInside.Models;

/// <summary>
/// 최근 사용한 스티커 히스토리 항목
/// </summary>
public class HistoryEntry
{
	/// <summary>콘텐츠 소스</summary>
	public ContentSource Source { get; set; }

	/// <summary>패키지 고유 식별자 (string 기반)</summary>
	public string PackageIdentifier { get; set; } = string.Empty;

	/// <summary>Legacy integer ID. 하위 호환을 위해 영구 유지 필요. 제거 금지.</summary>
	[System.Obsolete("Use PackageIdentifier. Do NOT remove — required for backward compatibility.")]
	public int PackageIndex { get; set; }

	/// <summary>스티커 경로 (패키지 내 고유 식별자)</summary>
	public string StickerPath { get; set; } = string.Empty;
}
