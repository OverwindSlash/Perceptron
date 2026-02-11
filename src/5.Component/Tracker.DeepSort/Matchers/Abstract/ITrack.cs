using System.Collections.Generic;
using System.Drawing;

namespace Tracker.DeepSort.Matchers.Abstract
{
    public interface ITrack
    {
        public int Id { get; set; }
        public Color Color { get; }
        public IReadOnlyList<RectangleF> History { get; }
        public RectangleF CurrentBoundingBox { get; }
        public int DetectionObjectType { get; }

        public void RegisterTracked(RectangleF trackedRectangle);
    }
}
