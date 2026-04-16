using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YellowInside.Models;

public class StoreProductResponse
{
    [JsonPropertyName("Payload")]
    public StoreProductPayload Payload { get; set; }
}

public class StoreProductPayload
{
    [JsonPropertyName("Notes")]
    public List<string> Notes { get; set; }
}

[JsonSerializable(typeof(StoreProductResponse))]
public partial class StoreProductJsonContext : JsonSerializerContext;
