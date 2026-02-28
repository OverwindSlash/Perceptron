using OpenCvSharp;
using System.Text.Json.Serialization;

namespace Algorithm.Ship.Labels;

public class ShipLabel
{
    public string ShipType { get; set; } = "Unknown";
    public List<string> ShipColor { get; set; } = new List<string> { "Unknown" };
    public string ShipDraught { get; set; } = "Unknown";
    public float Confidence { get; set; } = 0.0f;

    [JsonIgnore]
    public Mat Snapshot { get; set; }
    [JsonIgnore]
    public string JsonLabel { get; set; }
}