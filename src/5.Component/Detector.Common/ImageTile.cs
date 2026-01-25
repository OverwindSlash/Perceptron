namespace Detector.Common;

public class ImageTile
{
    // 子图在原图中的网格索引
    public int RowIndex { get; init; }
    public int ColIndex { get; init; }

    // 子图宽高（像素）
    public int TileWidth { get; init; }
    public int TileHeight { get; init; }

    // 该子图的预测结果
    public IReadOnlyList<YoloPrediction> Predictions { get; init; } = [];
}