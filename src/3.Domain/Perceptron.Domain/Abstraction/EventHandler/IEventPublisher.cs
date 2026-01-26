using MessagePipe;

namespace Perceptron.Domain.Abstraction.EventHandler
{
    public interface IEventPublisher<TEvent>
    {
        void SetPublisher(IPublisher<TEvent> publisher);
        void PublishEvent(TEvent @event);
    }
}
