using OpenCvSharp;
using System.Text.Json.Serialization;

namespace Algorithm.Ship.LabelsByLLM;

public class ShipLabel
{
    public string ShipTypeGroup { get; set; } = "Unknown";
    public string ShipTypeDetail { get; set; } = "Unknown";
    public List<string> ShipColor { get; set; } = new List<string> { };
    public string ShipDraught { get; set; } = "Unknown";
    public string ShipViewAngle { get; set; } = "Unknown";
    public List<string> ShipLoadTypes { get; set; } = new List<string> { };
    public List<PaintedText> ShipPaintedText { get; set; }
    
    public string DetectedObjectId { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0.0f;
    public string SourceId { get; set; } = string.Empty;
    public long FrameId { get; set; }
    public DateTime UtcTimeStamp { get; set; }

    [JsonIgnore]
    public Mat Snapshot { get; set; }
    [JsonIgnore]
    public string JsonLabel { get; set; }
}

public class PaintedText
{
    public string Text { get; set; } = string.Empty;
    public List<int> Bbox { get; set; } = new List<int> { 0, 0, 0, 0 };
}