using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YellowInside.Models;

/// <summary>
/// NativeAOT 호환을 위한 앱별 전송 방식 설정 JSON 소스 제너레이션 컨텍스트
/// </summary>
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
public partial class AppSendMethodSettingsJsonContext : JsonSerializerContext;
