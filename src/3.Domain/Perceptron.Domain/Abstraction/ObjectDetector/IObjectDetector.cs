using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.ObjectDetector;

public interface IObjectDetector : IDisposable
{
    void Init(List<IRegionManager> regionManagers);

    IReadOnlyList<DetectedObject> Detect(Frame frame, float confThresh, float iouThresh);

    IReadOnlyList<IReadOnlyList<DetectedObject>> DetectBatch(List<Frame> frames, float confThresh, float iouThresh);

    IReadOnlyList<DetectedObject> DetectByTile(Frame frame, Tuple<int, int> tileSettings, float confThresh, float iouThresh);

    void Close();
}