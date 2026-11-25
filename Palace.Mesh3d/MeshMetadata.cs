using System.Text.Json;
using System.Text.Json.Serialization;

namespace Palace.Mesh3d;

/// <summary>
/// 3D facts stored in <c>Asset.MetadataJson</c> (D3). Width/Height on the asset
/// row stay null — mosaic aspect comes from the thumbnail JPEG.
/// </summary>
public sealed class MeshMetadata
{
    public const string Schema = "palace-mesh/1";

    [JsonPropertyName("schema")]
    public string SchemaId { get; set; } = Schema;

    [JsonPropertyName("bboxMin")]
    public float[]? BBoxMin { get; set; }

    [JsonPropertyName("bboxMax")]
    public float[]? BBoxMax { get; set; }

    [JsonPropertyName("tris")]
    public int? TriangleCount { get; set; }

    [JsonPropertyName("bones")]
    public int? BoneCount { get; set; }

    [JsonPropertyName("clips")]
    public int? ClipCount { get; set; }

    [JsonPropertyName("deps")]
    public List<string>? Dependencies { get; set; }

    public static MeshMetadata? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MeshMetadata>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
