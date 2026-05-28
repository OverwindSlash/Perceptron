using OpenCvSharp;
using System.Text.Json.Serialization;
using Perceptron.Domain.Entity.VideoStream;

namespace Algorithm.Ship.Labels;

public class ShipLabel
{
    public string ShipTypeGroup { get; set; } = "Other";
    public List<string> ShipColor { get; set; } = new List<string> { "Other" };
    public string ShipDraught { get; set; } = "Unknown";
    public string ShipViewAngle { get; set; } = "Unknown";
    public float Confidence { get; set; } = 0.0f;

    [JsonIgnore]
    public Frame Frame { get; set; }
    [JsonIgnore]
    public Mat Snapshot { get; set; }
    [JsonIgnore]
    public string JsonLabel { get; set; }
}
