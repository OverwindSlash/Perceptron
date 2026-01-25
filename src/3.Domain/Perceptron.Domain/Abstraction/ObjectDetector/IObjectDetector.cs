using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.ObjectDetector;

public interface IObjectDetector : IDisposable
{
    void Init();

    IReadOnlyList<DetectedObject> Detect(Frame frame, float confThresh);

    IReadOnlyList<IReadOnlyList<DetectedObject>> DetectBatch(List<Frame> frames, float confThresh);

    IReadOnlyList<DetectedObject> DetectByTile(Frame frame, Tuple<int, int> tileSettings, float confThresh);

    void Close();
}