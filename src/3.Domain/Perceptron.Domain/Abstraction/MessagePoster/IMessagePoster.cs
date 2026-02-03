using Perceptron.Domain.Event;

namespace Perceptron.Domain.Abstraction.MessagePoster;

public interface IMessagePoster
{
    void PostDomainEventMessage(DomainEvent @event);
}