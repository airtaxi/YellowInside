using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YellowInside.Models;

/// <summary>
/// NativeAOT 호환을 위한 히스토리 매니저 JSON 소스 제너레이션 컨텍스트
/// </summary>
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
public partial class HistoryManagerJsonContext : JsonSerializerContext;
