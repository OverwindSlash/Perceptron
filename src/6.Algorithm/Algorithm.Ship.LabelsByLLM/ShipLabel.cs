using OpenCvSharp;
using System.Text.Json.Serialization;

namespace Algorithm.Ship.LabelsByLLM;

public class ShipLabel
{
    public string DetectedObjectId { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0.0f;
    public string ShipType { get; set; } = "Unknown";
    public List<string> ShipColor { get; set; } = new List<string> { "Unknown" };
    public string ShipDraught { get; set; } = "Unknown";
    public string SourceId { get; set; } = string.Empty;
    public long FrameId { get; set; }
    public DateTime UtcTimeStamp { get; set; }

    [JsonIgnore]
    public Mat Snapshot { get; set; }
    [JsonIgnore]
    public string JsonLabel { get; set; }
}
