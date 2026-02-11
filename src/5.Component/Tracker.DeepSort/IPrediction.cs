using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tracker.DeepSort
{
    public interface IPrediction
    {
        public int DetectionObjectType { get; }
        public Rectangle CurrentBoundingBox { get; }
        public float Confidence { get; }
    }
}
