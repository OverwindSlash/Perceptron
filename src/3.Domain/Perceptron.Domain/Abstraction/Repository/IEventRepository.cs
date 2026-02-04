using Perceptron.Domain.Event;

namespace Perceptron.Domain.Abstraction.Repository;

public interface IEventRepository
{
    Task SaveDomainEventAsync(DomainEvent domainEvent);
    Task<DomainEvent> LoadDomainEventAsync(string eventId);
    Task DeleteDomainEventAsync(string eventId);
}