namespace Detector.Common;

public enum ConfidenceAggregateMode { Max, Average }
public enum TrackingIdAggregateMode { MinNonZero, Zero }

public class MergeOptions
{
    public int MaxStitchGapPx { get; init; } = 12;              // 贴缝允许的最大缝隙
    public float MinOrthOverlapRatio { get; init; } = 0.5f;     // 垂直/水平方向的最小重合比例
    public bool RequireSameClass { get; init; } = true;
    public ConfidenceAggregateMode ConfidenceMode { get; init; } = ConfidenceAggregateMode.Max;
    public TrackingIdAggregateMode TrackingMode { get; init; } = TrackingIdAggregateMode.MinNonZero;
}

