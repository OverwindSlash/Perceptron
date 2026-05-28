namespace Algorithm.Ship.Labels;

public static class ShipLabelConfigs
{
    public static readonly string[] ShipTypes = new[]
    {
        "Cargo", "Tanker", "Passenger", "Workboat", "LawEnforcement",
        "Military", "Fishing", "Leisure", "Sailing", "Other"
    };

    public static readonly string[] ShipColors = new[]
    {
        "White", "Blue", "Gray", "Black", "Red",
        "Orange", "Yellow", "Green", "Brown", "Silver", "Other"
    };

    public static readonly string[] ShipDraughts = new[]
    {
        "Shallow", "Medium", "Deep", "Unknown"
    };

    public static readonly string[] ShipViewAngles = new[]
    {
        "Front", "Rear", "PortSide", "StarboardSide",
        "ObliqueFront", "ObliqueRear", "TopView", "Unknown"
    };
}
