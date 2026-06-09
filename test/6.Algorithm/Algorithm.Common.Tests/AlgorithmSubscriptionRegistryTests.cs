using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Algorithm.Common.Tests;

public class AlgorithmSubscriptionRegistryTests
{
    [Test]
    public void Subscribe_RegistersHandlerOnce()
    {
        using var provider = CreateProvider();
        using var registry = new AlgorithmSubscriptionRegistry();
        var subscriber = provider.GetRequiredService<ISubscriber<TestMessage>>();
        var publisher = provider.GetRequiredService<IPublisher<TestMessage>>();
        var received = 0;

        registry.Subscribe(subscriber, _ => received++);
        publisher.Publish(new TestMessage());

        Assert.That(received, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_ReleasesAllSubscriptions()
    {
        using var provider = CreateProvider();
        var registry = new AlgorithmSubscriptionRegistry();
        var firstSubscriber = provider.GetRequiredService<ISubscriber<TestMessage>>();
        var firstPublisher = provider.GetRequiredService<IPublisher<TestMessage>>();
        var secondSubscriber = provider.GetRequiredService<ISubscriber<OtherMessage>>();
        var secondPublisher = provider.GetRequiredService<IPublisher<OtherMessage>>();
        var received = 0;

        registry.Subscribe(firstSubscriber, _ => received++);
        registry.Subscribe(secondSubscriber, _ => received++);
        registry.Dispose();

        firstPublisher.Publish(new TestMessage());
        secondPublisher.Publish(new OtherMessage());

        Assert.That(received, Is.Zero);
    }

    [Test]
    public void Dispose_ContinuesWhenOneSubscriptionThrows()
    {
        var registry = new AlgorithmSubscriptionRegistry();
        var disposed = false;
        registry.Add(new ThrowingDisposable());
        registry.Add(new CallbackDisposable(() => disposed = true));

        Assert.DoesNotThrow(registry.Dispose);
        Assert.That(disposed, Is.True);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        return services.BuildServiceProvider();
    }

    private sealed record TestMessage;
    private sealed record OtherMessage;

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("expected");
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}
