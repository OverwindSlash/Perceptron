namespace Algorithm.Ship.Labels;

public static class ShipLabelConfigs
{
    public static readonly string[] ShipTypes = new[]
    {
        "Container", "Bulker", "Tanker", "RoRo", "CarCarrier",
        "LNG", "Cruise", "Ferry", "Liner", "Tug",
        "Fishing", "Engineering", "Research", "Rescue", "Supply",
        "Pilot", "Warship", "CoastGuard", "Police", "Yacht",
        "Speedboat", "Sail", "Unknown"
    };

    public static readonly string[] ShipColors = new[]
    {
        "White", "Blue", "Gray", "Black", "Red",
        "Orange", "Yellow", "Green", "Brown", "Silver", "Unknown"
    };

    public static readonly string[] ShipDraughts = new[]
    {
        "Shallow", "Medium", "Deep"
    };
}