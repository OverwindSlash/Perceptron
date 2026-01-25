namespace Detector.Common;

public class YoloDefaults
{
    public const float DefaultConfidenceThreshold = 0.25f;
    public const float DefaultIouThreshold = 0.5f;
    // 如果发现检测结果中 同一个物体上有多个框 ，可以尝试 调低 该值；如果发现 靠得很近的物体经常只检出一个 ，可以尝试 调高 该值。
}