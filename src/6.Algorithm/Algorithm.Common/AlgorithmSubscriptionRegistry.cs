using MessagePipe;
using Serilog;

namespace Algorithm.Common;

public sealed class AlgorithmSubscriptionRegistry : IDisposable
{
    private readonly object _sync = new();
    private readonly List<IDisposable> _subscriptions = [];
    private bool _isDisposed;

    public IDisposable Subscribe<TMessage>(
        ISubscriber<TMessage> subscriber,
        Action<TMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = subscriber.Subscribe(handler);
        Add(subscription);
        return subscription;
    }

    public void Add(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        lock (_sync)
        {
            if (_isDisposed)
            {
                subscription.Dispose();
                throw new ObjectDisposedException(nameof(AlgorithmSubscriptionRegistry));
            }

            _subscriptions.Add(subscription);
        }
    }

    public void Dispose()
    {
        List<IDisposable> subscriptions;
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to dispose an algorithm event subscription.");
            }
        }
    }
}
