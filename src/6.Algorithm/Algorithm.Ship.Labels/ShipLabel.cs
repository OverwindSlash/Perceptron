namespace Algorithm.Ship.Labels;

public class ShipLabel
{
    public string ShipTypeGroup { get; set; } = "Other";
    public string ShipTypeDetail { get; set; } = "Unknown";
    public List<string> ShipColor { get; set; } = new List<string> { "Other" };
    public float Confidence { get; set; } = 0.0f;
}
