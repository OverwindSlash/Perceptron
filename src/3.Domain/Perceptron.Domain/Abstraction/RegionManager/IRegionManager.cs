using Perceptron.Domain.Abstraction.EventHandler;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;

namespace Perceptron.Domain.Abstraction.RegionManager;

public interface IRegionManager : IEventSubscriber<ObjectExpiredEvent>
{
    public string RegionDefinitionFile { get; }
    public ImageRegionDefinition RegionDefinition { get; }
    public bool Initialized { get; }

    void InitRegionDefinition(int imageWidth, int imageHeight);
    void CalcRegionProperties(Frame frame);
}