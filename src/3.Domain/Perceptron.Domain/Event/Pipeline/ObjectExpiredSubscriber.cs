using MessagePipe;
using Perceptron.Domain.Abstraction.EventHandler;

namespace Perceptron.Domain.Pipeline;

public abstract class ObjectExpiredSubscriber : IEventSubscriber<ObjectExpiredEvent>
{
    private ISubscriber<ObjectExpiredEvent> _oeSubscriber;
    private IDisposable _disposableOeSubscriber;

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _oeSubscriber = subscriber;
        _disposableOeSubscriber = _oeSubscriber.Subscribe(ProcessEvent);
    }

    public abstract void ProcessEvent(ObjectExpiredEvent @event);

    public virtual void Dispose()
    {
        _disposableOeSubscriber.Dispose();
    }
}