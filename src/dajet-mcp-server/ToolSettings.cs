using System.Text.Json.Serialization;

namespace DaJet.Mcp.Server
{
    public sealed class ToolSettings
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    }
}