using System.Collections;
using System.Diagnostics;

namespace Perceptron.Domain.DataStructure;

public class ConcurrentBoundedQueue<T> : IConcurrentBoundedQueue<T>
    where T : class, IDisposable
{
    private readonly T?[] _buffer;
    private readonly object _lock = new();
    private int _head;
    private int _tail;
    private int _count;
    private bool _isDisposed;

    // Metrics
    private long _totalEnqueued;
    private long _totalDequeued;
    private long _totalOverwritten;
    private long _totalDropped;
    private int _maxUtilization;
    
    private readonly Stopwatch _stopwatch;

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public bool IsFull
    {
        get
        {
            lock (_lock)
            {
                return _count == Capacity;
            }
        }
    }

    public double Utilization
    {
        get
        {
            lock (_lock)
            {
                return (double)_count / Capacity;
            }
        }
    }

    public long TotalEnqueued
    {
        get
        {
            lock (_lock)
            {
                return _totalEnqueued;
            }
        }
    }

    public long TotalDequeued
    {
        get
        {
            lock (_lock)
            {
                return _totalDequeued;
            }
        }
    }

    public long TotalOverwritten
    {
        get
        {
            lock (_lock)
            {
                return _totalOverwritten;
            }
        }
    }

    public long TotalDropped
    {
        get
        {
            lock (_lock)
            {
                return _totalDropped;
            }
        }
    }

    public int MaxUtilization
    {
        get
        {
            lock (_lock)
            {
                return _maxUtilization;
            }
        }
    }

    public double EnqueueThroughput
    {
        get
        {
            double elapsed = Math.Max(_stopwatch.Elapsed.TotalSeconds, 1e-6);
            lock (_lock)
            {
                return _totalEnqueued / elapsed;
            }
        }
    }

    public double DequeueThroughput
    {
        get
        {
            double elapsed = Math.Max(_stopwatch.Elapsed.TotalSeconds, 1e-6);
            lock (_lock)
            {
                return _totalDequeued / elapsed;
            }
        }
    }

    public event Action<T>? OnItemDropped;

    public ConcurrentBoundedQueue(int capacity, Action<T>? onItemDropped = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;
        _buffer = new T[capacity];
        _stopwatch = Stopwatch.StartNew();

        if (onItemDropped != null)
        {
            OnItemDropped += onItemDropped;
        }
    }

    public void Enqueue(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        T? itemToDrop = null;

        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ConcurrentBoundedQueue<T>));

            if (_count < Capacity)
            {
                _buffer[_tail] = item;
                _tail = (_tail + 1) % Capacity;
                _count++;
                _totalEnqueued++;
                if (_count > _maxUtilization)
                {
                    _maxUtilization = _count;
                }
            }
            else
            {
                // Full - overwrite oldest
                itemToDrop = _buffer[_head];
                
                _buffer[_head] = item;
                _head = (_head + 1) % Capacity;
                _tail = _head; // Tail always follows head when full (points to next write pos)
                
                // Count remains Capacity
                _totalEnqueued++;
                _totalOverwritten++;
            }
        }

        if (itemToDrop != null)
        {
            HandleItemDropped(itemToDrop);
        }
    }

    public void EnqueueRange(IEnumerable<T> items)
    {
        if (items == null) return;

        foreach (var item in items)
        {
            if (item != null)
            {
                Enqueue(item);
            }
        }
    }

    public bool TryDequeue(out T? item)
    {
        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ConcurrentBoundedQueue<T>));

            if (_count == 0)
            {
                item = null;
                return false;
            }

            item = _buffer[_head];
            _buffer[_head] = null; // Avoid memory leak
            _head = (_head + 1) % Capacity;
            _count--;
            _totalDequeued++;
            
            return true;
        }
    }

    public bool TryPeek(out T? item)
    {
        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ConcurrentBoundedQueue<T>));

            if (_count == 0)
            {
                item = null;
                return false;
            }

            item = _buffer[_head];
            return true;
        }
    }

    public void Clear()
    {
        List<T> itemsToDrop = new List<T>();

        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ConcurrentBoundedQueue<T>));

            if (_count == 0) return;

            // Collect all items to drop in FIFO order
            int current = _head;
            for (int i = 0; i < _count; i++)
            {
                var val = _buffer[current];
                if (val != null)
                {
                    itemsToDrop.Add(val);
                    _buffer[current] = null;
                }
                current = (current + 1) % Capacity;
            }

            _totalDropped += itemsToDrop.Count;
            _count = 0;
            _head = 0;
            _tail = 0;
        }

        foreach (var item in itemsToDrop)
        {
            HandleItemDropped(item);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        List<T> snapshot = new List<T>();

        lock (_lock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ConcurrentBoundedQueue<T>));

            int current = _head;
            for (int i = 0; i < _count; i++)
            {
                var val = _buffer[current];
                if (val != null)
                {
                    snapshot.Add(val);
                }
                current = (current + 1) % Capacity;
            }
        }

        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Dispose()
    {
        List<T> itemsToDrop = new List<T>();

        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _stopwatch.Stop();

            // Perform Clear logic
            if (_count > 0)
            {
                int current = _head;
                for (int i = 0; i < _count; i++)
                {
                    var val = _buffer[current];
                    if (val != null)
                    {
                        itemsToDrop.Add(val);
                        _buffer[current] = null;
                    }
                    current = (current + 1) % Capacity;
                }
                
                _totalDropped += itemsToDrop.Count;
                _count = 0;
                _head = 0;
                _tail = 0;
            }
        }

        foreach (var item in itemsToDrop)
        {
            HandleItemDropped(item);
        }
    }

    private void HandleItemDropped(T item)
    {
        if (OnItemDropped != null)
        {
            foreach (Action<T> handler in OnItemDropped.GetInvocationList())
            {
                try
                {
                    handler(item);
                }
                catch
                {
                    // Swallow exception as per spec suggestion: isolate subscribers
                }
            }
        }

        try
        {
            item.Dispose();
        }
        catch
        {
            // Swallow exception as per best practice
        }
    }
}
