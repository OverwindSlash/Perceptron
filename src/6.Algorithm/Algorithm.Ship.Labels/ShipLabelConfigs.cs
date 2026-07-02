namespace Algorithm.Ship.Labels;

public static class ShipLabelConfigs
{
    public static readonly string[] ShipTypes = new[]
    {
        "Cargo", "Tanker", "Passenger", "Workboat", "LawEnforcement",
        "Military", "Fishing", "Leisure", "Sailing", "Other", "Unknown"
    };

    public static readonly string[] ShipTypeDetails = new[]
    {
        "Container", "Bulker", "RoRo", "Tanker", "LNG",
        "Cruise", "Ferry", "Tug", "Engineering", "Research",
        "Rescue", "Supply", "Pilot", "Warship", "CoastGuard",
        "Police", "Fishing", "Yacht", "Speedboat", "Sail",
        "Other", "Unknown"
    };

    public static readonly string[] ShipColors = new[]
    {
        "White", "Blue", "Gray", "Black", "Red",
        "Orange", "Yellow", "Green", "Brown", "Silver", "Other"
    };

    public static readonly Dictionary<string, string[]> ShipTypeDetailCompatibility = new()
    {
        ["Cargo"] = new[] { "Container", "Bulker", "RoRo", "Unknown" },
        ["Tanker"] = new[] { "Tanker", "LNG", "Unknown" },
        ["Passenger"] = new[] { "Cruise", "Ferry", "Unknown" },
        ["Workboat"] = new[] { "Tug", "Engineering", "Research", "Rescue", "Supply", "Pilot", "Unknown" },
        ["LawEnforcement"] = new[] { "CoastGuard", "Police", "Unknown" },
        ["Military"] = new[] { "Warship", "Unknown" },
        ["Fishing"] = new[] { "Fishing", "Unknown" },
        ["Leisure"] = new[] { "Yacht", "Speedboat", "Unknown" },
        ["Sailing"] = new[] { "Sail", "Unknown" },
        ["Other"] = new[] { "Other", "Unknown" },
        ["Unknown"] = new[] { "Unknown" }
    };
}
