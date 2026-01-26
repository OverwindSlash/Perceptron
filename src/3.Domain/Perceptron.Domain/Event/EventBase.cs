namespace Perceptron.Domain.Event;

public abstract class EventBase
{
    public Guid EventId { get; }

    public DateTime Timestamp { get; }

    public EventBase()
    {
        EventId = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }
}