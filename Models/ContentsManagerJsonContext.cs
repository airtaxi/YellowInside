using System.Text.Json.Serialization;

namespace YellowInside.Models;

/// <summary>
/// NativeAOT 호환을 위한 System.Text.Json 소스 제너레이션 컨텍스트
/// </summary>
[JsonSerializable(typeof(ContentsManagerData))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class ContentsManagerJsonContext : JsonSerializerContext;
