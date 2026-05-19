using OpenCvSharp;
using Tracker.DeepSort.Utils.DataStructs;

namespace Tracker.DeepSort.ReID;

public interface IAppearanceExtractor : IDisposable
{
    public abstract IReadOnlyList<Vector> Predict(Mat image, IPrediction[] detectedBounds);
}