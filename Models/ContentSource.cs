namespace YellowInside.Models;

/// <summary>
/// 콘텐츠 소스 유형 (디시콘, 아카콘 지원)
/// </summary>
public enum ContentSource : int
{
	Dccon = 0,
	Arcacon = 1,
	Local = -1 // -1로 설정하여 추후 다른 소스가 추가되더라도 기존 값이 변경되지 않도록 함
}

public static class ContentSourceExtensions
{
	public static string GetFriendlyName(this ContentSource source)
	{
		return source switch
		{
			ContentSource.Dccon => "디시콘",
			ContentSource.Arcacon => "아카콘",
			ContentSource.Local => "사용자 지정콘",
			_ => "알 수 없음"
		};
	}
}