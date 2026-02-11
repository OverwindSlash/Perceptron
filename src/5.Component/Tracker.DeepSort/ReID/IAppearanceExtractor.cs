using MOT.CORE.Utils.DataStructs;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using Tracker.DeepSort;

namespace MOT.CORE.ReID
{
    public interface IAppearanceExtractor : IDisposable
    {
        public abstract IReadOnlyList<Vector> Predict(Mat image, IPrediction[] detectedBounds);
    }
}
