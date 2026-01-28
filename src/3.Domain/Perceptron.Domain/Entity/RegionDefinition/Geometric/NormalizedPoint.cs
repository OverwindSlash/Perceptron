using System.Text.Json.Serialization;

namespace Perceptron.Domain.Entity.RegionDefinition.Geometric;

public class NormalizedPoint : ImageBasedGeometric
{
    private int _originalX;
    private int _originalY;

    private double _normalizedX;
    private double _normalizedY;

    [JsonIgnore]
    public int OriginalX
    {
        get
        {
            CheckImageSizeInitialized();
            return _originalX;
        }
    }

    [JsonIgnore]
    public int OriginalY
    {
        get
        {
            CheckImageSizeInitialized();
            return _originalY;
        }
    }

    public double NormalizedX
    {
        // get
        // {
        //     CheckImageSizeInitialized();
        //     return _normalizedX;
        // }
        get => _normalizedX;
        set => _normalizedX = value;
    }

    public double NormalizedY
    {
        // get
        // {
        //     CheckImageSizeInitialized();
        //     return _normalizedY;
        // }
        get => _normalizedY;
        set => _normalizedY = value;
    }

    // For point generation by json deserialization
    // 1. Use constructor to create a new NormalizedPoint object.
    // 2. Use Properties set method to set _normalizedX and _normalizedY.
    // 3. Manual call SetImageSize to specify real image width and height to calculate _originalX and _originalY
    public NormalizedPoint()
    {
        _normalizedX = 0;
        _normalizedY = 0;
    }

    public NormalizedPoint(double normalizedX, double normalizedY)
    {
        _normalizedX = normalizedX;
        _normalizedY = normalizedY;
    }

    public override void SetImageSize(int width, int height)
    {
        base.SetImageSize(width, height);

        _originalX = (int)Math.Round(width * _normalizedX);
        _originalY = (int)Math.Round(height * _normalizedY);
    }

    // For point generation by hand, specify image length, width and point x, y
    public NormalizedPoint(int imageWidth, int imageHeight, int originalX, int originalY)
        : base(imageWidth, imageHeight)
    {
        _originalX = originalX;
        _originalY = originalY;

        _normalizedX = (double)_originalX / imageWidth;
        _normalizedY = (double)_originalY / imageHeight;
    }

    public double DistanceTo(NormalizedPoint other)
    {
        double xDiff = OriginalX - other.OriginalX;
        double yDiff = OriginalY - other.OriginalY;
        return Math.Sqrt(xDiff * xDiff + yDiff * yDiff);
    }
}