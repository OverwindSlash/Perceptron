using System.Drawing;

namespace Tracker.Sort;

public interface ITracker
{
    IEnumerable<Track> Track(IEnumerable<RectangleF> boxes);
}