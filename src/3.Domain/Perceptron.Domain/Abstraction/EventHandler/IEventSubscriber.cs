using MessagePipe;

namespace Perceptron.Domain.Abstraction.EventHandler;

public interface IEventSubscriber<TEvent> : IDisposable
{
    void SetSubscriber(ISubscriber<TEvent> subscriber);
    void ProcessEvent(TEvent @event);
}