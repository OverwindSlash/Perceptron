using Perceptron.Domain.Entity.VideoStream;

namespace Perceptron.Domain.Abstraction.ObjectTracker;

public interface IObjectTracker
{
    void Track(Frame frame);
}