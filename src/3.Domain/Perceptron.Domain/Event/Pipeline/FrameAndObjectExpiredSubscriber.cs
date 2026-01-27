using MessagePipe;
using Perceptron.Domain.Abstraction.EventHandler;

namespace Perceptron.Domain.Event.Pipeline;

public abstract class FrameAndObjectExpiredSubscriber : IEventSubscriber<FrameExpiredEvent>, IEventSubscriber<ObjectExpiredEvent>
{
    private ISubscriber<FrameExpiredEvent>? _feSubscriber;
    private IDisposable? _disposableFeSubscriber;

    private ISubscriber<ObjectExpiredEvent>? _oeSubscriber;
    private IDisposable? _disposableOeSubscriber;

    public void SetSubscriber(ISubscriber<FrameExpiredEvent> subscriber)
    {
        _feSubscriber = subscriber;
        _disposableFeSubscriber = _feSubscriber.Subscribe(ProcessEvent);
    }

    public abstract void ProcessEvent(FrameExpiredEvent @event);

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _oeSubscriber = subscriber;
        _disposableOeSubscriber = _oeSubscriber.Subscribe(ProcessEvent);
    }

    public abstract void ProcessEvent(ObjectExpiredEvent @event);

    public virtual void Dispose()
    {
        _disposableFeSubscriber?.Dispose();
        _disposableOeSubscriber?.Dispose();
    }
}