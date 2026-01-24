namespace Perceptron.Domain.DataStructure;

public interface IConcurrentBoundedQueue<T> : IEnumerable<T>, IDisposable
    where T : class, IDisposable
{
    int Count { get; }
    int Capacity { get; }
    bool IsFull { get; }
    double Utilization { get; }

    long TotalEnqueued { get; }
    long TotalDequeued { get; }
    long TotalOverwritten { get; }
    long TotalDropped { get; }
    int MaxUtilization { get; }

    double EnqueueThroughput { get; }
    double DequeueThroughput { get; }

    event Action<T>? OnItemDropped;

    void Enqueue(T item);
    void EnqueueRange(IEnumerable<T> items);
    bool TryDequeue(out T? item);
    bool TryPeek(out T? item);
    void Clear();
}
