namespace YellowInside.Models;

/// <summary>
/// 개별 스티커 정보 (소스 비종속)
/// </summary>
public class Sticker
{
	/// <summary>이미지 경로 (고유 식별자)</summary>
	public string Path { get; set; } = string.Empty;

	/// <summary>스티커 제목</summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>파일 확장자 (png, gif, jpg 등)</summary>
	public string Extension { get; set; } = string.Empty;

	/// <summary>정렬 순번</summary>
	public int SortNumber { get; set; }

	/// <summary>스티커 이미지의 전체 다운로드 URL</summary>
	public string ImageUrl { get; set; } = string.Empty;

	/// <summary>로컬에 저장된 파일명 (확장자 포함)</summary>
	public string FileName { get; set; } = string.Empty;
}
