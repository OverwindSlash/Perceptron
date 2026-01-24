using Perceptron.Domain.DataStructure;

namespace Perceptron.Domain.Tests.DataStructure;

[TestFixture]
public class ConcurrentBoundedQueueTests
{
    private ConcurrentBoundedQueue<MockDisposable> _queue;
    private const int DefaultCapacity = 5;

    public class MockDisposable : IDisposable
    {
        public int Id { get; }
        public bool IsDisposed { get; private set; }
        public virtual void Dispose()
        {
            IsDisposed = true;
        }

        public MockDisposable(int id)
        {
            Id = id;
        }
    }

    [SetUp]
    public void Setup()
    {
        _queue = new ConcurrentBoundedQueue<MockDisposable>(DefaultCapacity);
    }

    [TearDown]
    public void TearDown()
    {
        _queue?.Dispose();
    }

    #region 10.1 构造与参数

    [Test]
    public void Constructor_InvalidCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentBoundedQueue<MockDisposable>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConcurrentBoundedQueue<MockDisposable>(-1));
    }

    [Test]
    public void Constructor_OnItemDropped_SubscribesCallback()
    {
        bool callbackInvoked = false;
        var queue = new ConcurrentBoundedQueue<MockDisposable>(1, _ => callbackInvoked = true);
        
        // 触发丢弃：入队2个元素
        queue.Enqueue(new MockDisposable(1));
        queue.Enqueue(new MockDisposable(2)); // 此时应丢弃 1

        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public void Enqueue_NullItem_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _queue.Enqueue(null!));
    }

    #endregion

    #region 10.2 基本入队/出队

    [Test]
    public void Enqueue_IncrementsCount()
    {
        _queue.Enqueue(new MockDisposable(1));
        Assert.That(_queue.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryDequeue_FIFO_Order()
    {
        var item1 = new MockDisposable(1);
        var item2 = new MockDisposable(2);

        _queue.Enqueue(item1);
        _queue.Enqueue(item2);

        bool result1 = _queue.TryDequeue(out var dequeued1);
        bool result2 = _queue.TryDequeue(out var dequeued2);

        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(dequeued1, Is.EqualTo(item1));
            Assert.That(result2, Is.True);
            Assert.That(dequeued2, Is.EqualTo(item2));
            Assert.That(_queue.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryDequeue_EmptyQueue_ReturnsFalse()
    {
        bool result = _queue.TryDequeue(out var item);
        Assert.That(result, Is.False);
        Assert.That(item, Is.Null);
    }

    #endregion

    #region 10.3 覆盖语义

    [Test]
    public void Enqueue_FullCapacity_OverwritesOldestAndDisposes()
    {
        // Capacity is 5
        var items = Enumerable.Range(0, 6).Select(i => new MockDisposable(i)).ToArray();
        MockDisposable? droppedItem = null;

        _queue.OnItemDropped += (item) => droppedItem = item;

        // Fill queue
        for (int i = 0; i < 5; i++)
        {
            _queue.Enqueue(items[i]);
        }

        // Overwrite oldest (items[0])
        _queue.Enqueue(items[5]);

        Assert.Multiple(() =>
        {
            Assert.That(_queue.Count, Is.EqualTo(5));
            Assert.That(_queue.TotalOverwritten, Is.EqualTo(1));
            Assert.That(_queue.TotalEnqueued, Is.EqualTo(6));
            
            // Verify dropped item
            Assert.That(droppedItem, Is.Not.Null);
            Assert.That(droppedItem!.Id, Is.EqualTo(items[0].Id));
            Assert.That(items[0].IsDisposed, Is.True);
            
            // Verify current queue content (should be 1..5)
            var currentItems = _queue.Select(x => x.Id).ToList();
            Assert.That(currentItems, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
        });
    }

    #endregion

    #region 10.4 Clear/Dispose 释放语义

    [Test]
    public void Clear_DisposesAllItemsAndFiresCallbacks()
    {
        var item1 = new MockDisposable(1);
        var item2 = new MockDisposable(2);
        var droppedItems = new List<MockDisposable>();

        _queue.OnItemDropped += (item) => droppedItems.Add(item);
        _queue.Enqueue(item1);
        _queue.Enqueue(item2);

        _queue.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(_queue.Count, Is.Zero);
            Assert.That(_queue.TotalDropped, Is.EqualTo(2));
            Assert.That(droppedItems.Count, Is.EqualTo(2));
            Assert.That(droppedItems[0], Is.EqualTo(item1));
            Assert.That(droppedItems[1], Is.EqualTo(item2));
            Assert.That(item1.IsDisposed, Is.True);
            Assert.That(item2.IsDisposed, Is.True);
        });
    }

    [Test]
    public void Dispose_Idempotent_And_ClearsQueue()
    {
        var item = new MockDisposable(1);
        _queue.Enqueue(item);

        _queue.Dispose();
        _queue.Dispose(); // Second call should not throw

        Assert.That(item.IsDisposed, Is.True);
        
        // Accessing methods should throw
        Assert.Throws<ObjectDisposedException>(() => _queue.Enqueue(new MockDisposable(2)));
    }

    [Test]
    public void Dispose_AllowsReadingMetrics()
    {
        _queue.Enqueue(new MockDisposable(1));
        _queue.Dispose();

        // Should not throw
        Assert.DoesNotThrow(() =>
        {
            var c = _queue.Count;
            var t = _queue.TotalEnqueued;
        });
    }

    #endregion

    #region 10.5 回调异常隔离

    [Test]
    public void Enqueue_CallbackThrows_DoesNotAffectQueueState()
    {
        var queue = new ConcurrentBoundedQueue<MockDisposable>(1);
        queue.OnItemDropped += (item) => throw new Exception("Callback Error");

        var item1 = new MockDisposable(1);
        var item2 = new MockDisposable(2);

        queue.Enqueue(item1);
        
        // Should not throw and swallow exception
        Assert.DoesNotThrow(() => queue.Enqueue(item2));
        
        Assert.That(queue.Count, Is.EqualTo(1));
        // item1 should still be disposed even if callback threw
        Assert.That(item1.IsDisposed, Is.True);
    }

    #endregion

    #region 10.6 快照枚举

    [Test]
    public void GetEnumerator_ReturnsSnapshot()
    {
        var item1 = new MockDisposable(1);
        var item2 = new MockDisposable(2);
        _queue.Enqueue(item1);
        _queue.Enqueue(item2);

        using var enumerator = _queue.GetEnumerator();
        
        // Modify queue after getting enumerator
        _queue.TryDequeue(out _);
        _queue.Enqueue(new MockDisposable(3));

        var snapshotList = new List<MockDisposable>();
        while (enumerator.MoveNext())
        {
            snapshotList.Add(enumerator.Current);
        }

        Assert.That(snapshotList.Select(x => x.Id), Is.EquivalentTo(new[] { 1, 2 }));
    }

    #endregion
    
    #region 10.7 并发压力测试

    [Test]
    public void Concurrency_MultiProducerMultiConsumer_DataIntegrity()
    {
        // 模拟多生产者多消费者高并发场景
        // 验证：
        // 1. 不抛出异常
        // 2. Count 不越界
        // 3. 资源释放正确（TotalEnqueued = TotalDequeued + TotalOverwritten + TotalDropped + Count）
        // 4. 不出现重复引用或空洞（隐式验证）

        int capacity = 100;
        int producerCount = 5;
        int consumerCount = 5;
        int itemsPerProducer = 10000;
        int totalItems = producerCount * itemsPerProducer;

        // 重置队列，使用更大的容量
        _queue.Dispose();
        _queue = new ConcurrentBoundedQueue<MockDisposable>(capacity);

        long disposedCount = 0;
        _queue.OnItemDropped += (item) => Interlocked.Increment(ref disposedCount);

        var tasks = new List<Task>();
        var startSignal = new ManualResetEventSlim(false);

        // Producers
        for (int i = 0; i < producerCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                startSignal.Wait();
                for (int j = 0; j < itemsPerProducer; j++)
                {
                    _queue.Enqueue(new MockDisposable(j));
                }
            }));
        }

        // Consumers
        var cts = new CancellationTokenSource();
        long dequeuedCount = 0;
        
        for (int i = 0; i < consumerCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                startSignal.Wait(cts.Token);
                while (!cts.Token.IsCancellationRequested)
                {
                    if (_queue.TryDequeue(out var item))
                    {
                        Interlocked.Increment(ref dequeuedCount);
                        // 模拟处理耗时，增加竞争
                        // Thread.SpinWait(10); 
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }, cts.Token));
        }

        // Start all
        startSignal.Set();

        // Wait for producers to finish
        Task.WaitAll(tasks.Take(producerCount).ToArray(), cts.Token);

        // Wait a bit for consumers to drain what they can
        Thread.Sleep(500);
        cts.Cancel();
        
        // Wait for consumers to stop
        try
        {
            Task.WaitAll(tasks.Skip(producerCount).ToArray());
        }
        catch (AggregateException) { /* Expected cancellation */ }

        // Final cleanup
        _queue.Dispose();

        // Verification
        long overwritten = _queue.TotalOverwritten;
        long dropped = _queue.TotalDropped; // from final Dispose
        long enqueued = _queue.TotalEnqueued;
        long count = _queue.Count; // Should be 0 after Dispose

        Console.WriteLine($"Total Enqueued: {enqueued}");
        Console.WriteLine($"Total Dequeued: {dequeuedCount}");
        Console.WriteLine($"Total Overwritten: {overwritten}");
        Console.WriteLine($"Total Dropped (Clear/Dispose): {dropped}");
        Console.WriteLine($"Callback Invoked Count: {disposedCount}");

        Assert.Multiple(() =>
        {
            Assert.That(enqueued, Is.EqualTo(totalItems), "TotalEnqueued should match expected input");
            Assert.That(count, Is.EqualTo(0), "Queue should be empty after Dispose");
            
            // 核心守恒公式：入队总数 = 出队数 + 覆盖数 + 剩余丢弃数
            // 注意：Dispose 后 Count 归零，所有剩余元素计入 TotalDropped
            Assert.That(enqueued, Is.EqualTo(dequeuedCount + overwritten + dropped), 
                "Invariant: Enqueued = Dequeued + Overwritten + Dropped");

            // 回调次数验证：覆盖的 + 最终清理的 = 总 Dispose 次数
            // 注意：出队的元素不由队列 Dispose，所以不计入 disposedCount
            Assert.That(disposedCount, Is.EqualTo(overwritten + dropped), 
                "Callback count should match dropped + overwritten items");
        });
    }

    #endregion

    #region 10.8 监控指标 (Metrics)

    [Test]
    public void Metrics_Utilization_CalculatesCorrectly()
    {
        // Capacity = 5 (Default in Setup)
        Assert.That(_queue.Utilization, Is.EqualTo(0));

        _queue.Enqueue(new MockDisposable(1));
        Assert.That(_queue.Utilization, Is.EqualTo(0.2).Within(0.0001)); // 1/5

        _queue.Enqueue(new MockDisposable(2));
        Assert.That(_queue.Utilization, Is.EqualTo(0.4).Within(0.0001)); // 2/5
    }

    [Test]
    public void Metrics_MaxUtilization_UpdatesOnPeak()
    {
        Assert.That(_queue.MaxUtilization, Is.EqualTo(0));

        _queue.Enqueue(new MockDisposable(1));
        _queue.Enqueue(new MockDisposable(2));
        Assert.That(_queue.MaxUtilization, Is.EqualTo(2));

        _queue.TryDequeue(out _);
        Assert.That(_queue.Count, Is.EqualTo(1));
        Assert.That(_queue.MaxUtilization, Is.EqualTo(2)); // Should stay at peak
    }

    [Test]
    public void Metrics_Throughput_NonNegative()
    {
        _queue.Enqueue(new MockDisposable(1));
        _queue.TryDequeue(out _);
        
        // Throughput = Total / Elapsed. Since Elapsed > 0, result >= 0
        Assert.That(_queue.EnqueueThroughput, Is.GreaterThanOrEqualTo(0));
        Assert.That(_queue.DequeueThroughput, Is.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region 11.0 补充功能 (TryPeek/EnqueueRange)

    [Test]
    public void TryPeek_ReturnsHeadWithoutRemoving()
    {
        var item1 = new MockDisposable(1);
        _queue.Enqueue(item1);

        bool result = _queue.TryPeek(out var peeked);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(peeked, Is.EqualTo(item1));
            Assert.That(_queue.Count, Is.EqualTo(1)); // Still in queue
        });

        // Ensure we can still dequeue it
        _queue.TryDequeue(out var dequeued);
        Assert.That(dequeued, Is.EqualTo(item1));
    }

    [Test]
    public void TryPeek_EmptyQueue_ReturnsFalse()
    {
        bool result = _queue.TryPeek(out var item);
        Assert.That(result, Is.False);
        Assert.That(item, Is.Null);
    }

    [Test]
    public void EnqueueRange_AddsAllItems()
    {
        var items = new[] { new MockDisposable(1), new MockDisposable(2), new MockDisposable(3) };
        _queue.EnqueueRange(items);

        Assert.That(_queue.Count, Is.EqualTo(3));
        
        // Verify order
        _queue.TryDequeue(out var d1);
        _queue.TryDequeue(out var d2);
        _queue.TryDequeue(out var d3);

        Assert.Multiple(() =>
        {
            Assert.That(d1!.Id, Is.EqualTo(1));
            Assert.That(d2!.Id, Is.EqualTo(2));
            Assert.That(d3!.Id, Is.EqualTo(3));
        });
    }

    [Test]
    public void EnqueueRange_SkipsNulls()
    {
        var items = new MockDisposable?[] { new MockDisposable(1), null, new MockDisposable(2) };
        
        // Should not throw
        Assert.DoesNotThrow(() => _queue.EnqueueRange(items!));

        Assert.That(_queue.Count, Is.EqualTo(2));
        
        _queue.TryDequeue(out var d1);
        _queue.TryDequeue(out var d2);

        Assert.That(d1!.Id, Is.EqualTo(1));
        Assert.That(d2!.Id, Is.EqualTo(2));
    }

    #endregion
}
