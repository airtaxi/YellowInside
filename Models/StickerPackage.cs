using System.Collections.Generic;

namespace YellowInside.Models;

/// <summary>
/// 스티커 패키지 정보 (소스 비종속)
/// </summary>
public class StickerPackage
{
	/// <summary>콘텐츠 소스</summary>
	public ContentSource Source { get; set; }

	/// <summary>패키지 고유 식별자 (string 기반, 다양한 소스 지원)</summary>
	public string PackageIdentifier { get; set; } = string.Empty;

	/// <summary>Legacy integer ID. .yip 임포트 하위 호환을 위해 영구 유지 필요. 제거 금지.</summary>
	[System.Obsolete("Use PackageIdentifier. Do NOT remove — required for .yip backward compatibility.")]
	public int PackageIndex { get; set; }

	/// <summary>패키지명</summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>설명</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>대표 이미지 경로</summary>
	public string MainImagePath { get; set; } = string.Empty;

	/// <summary>로컬에 저장된 대표 이미지 파일명</summary>
	public string MainImageFileName { get; set; } = string.Empty;

	/// <summary>제작자 이름</summary>
	public string SellerName { get; set; } = string.Empty;

	/// <summary>등록일</summary>
	public string RegistrationDate { get; set; } = string.Empty;

	/// <summary>패키지에 포함된 스티커 목록</summary>
	public List<Sticker> Stickers { get; set; } = [];

	/// <summary>태그 목록</summary>
	public List<string> Tags { get; set; } = [];

	/// <summary>로컬 저장 시 사용된 서브폴더명</summary>
	public string LocalDirectoryName { get; set; } = string.Empty;

	/// <summary>로컬에 저장된 패키지 디렉토리의 전체 경로</summary>
	public string LocalDirectoryPath { get; set; } = string.Empty;
}
