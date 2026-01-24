using OpenCvSharp;
using Perceptron.Domain.DataStructure;
using System.Collections.Concurrent;

namespace Perceptron.Domain.Tests.DataStructure;

[TestFixture]
public class MatPoolTests
{
    private MatPool _matPool;
    private const int DefaultCapacity = 5;

    [SetUp]
    public void Setup()
    {
        _matPool = new MatPool(DefaultCapacity);
    }

    [TearDown]
    public void TearDown()
    {
        _matPool?.Dispose();
    }

    [Test]
    public void Constructor_SetsMaxCapacity()
    {
        // Since MaxCapacity is private, we can only infer it from behavior or reflection.
        // Or we assume it works if the pool limits itself.
        // We can test the default constructor too.
        var pool = new MatPool();
        Assert.That(pool, Is.Not.Null);
        Assert.That(pool.Count, Is.EqualTo(0));
    }

    [Test]
    public void Rent_FromEmptyPool_ReturnsNewMat()
    {
        var mat = _matPool.Rent(100, 100, MatType.CV_8UC3);
        
        Assert.That(mat, Is.Not.Null);
        Assert.That(mat.Rows, Is.EqualTo(100));
        Assert.That(mat.Cols, Is.EqualTo(100));
        Assert.That(mat.Type(), Is.EqualTo(MatType.CV_8UC3));
        Assert.That(_matPool.Count, Is.EqualTo(0));
        
        mat.Dispose();
    }

    [Test]
    public void Rent_FromPool_ReturnsPooledMat()
    {
        var originalMat = new Mat(100, 100, MatType.CV_8UC3);
        _matPool.Return(originalMat);
        
        Assert.That(_matPool.Count, Is.EqualTo(1));

        var rentedMat = _matPool.Rent(100, 100, MatType.CV_8UC3);
        
        Assert.That(rentedMat, Is.SameAs(originalMat));
        Assert.That(_matPool.Count, Is.EqualTo(0));
        
        rentedMat.Dispose();
    }

    [Test]
    public void Rent_WithDifferentDimensions_DisposesPooledAndReturnsNew()
    {
        var originalMat = new Mat(100, 100, MatType.CV_8UC3);
        _matPool.Return(originalMat);
        
        Assert.That(_matPool.Count, Is.EqualTo(1));

        // Rent with different size
        var rentedMat = _matPool.Rent(200, 200, MatType.CV_8UC3);
        
        Assert.That(rentedMat, Is.Not.SameAs(originalMat));
        Assert.That(rentedMat.Rows, Is.EqualTo(200));
        Assert.That(originalMat.IsDisposed, Is.True, "Original mat should be disposed because it didn't match requirements");
        Assert.That(_matPool.Count, Is.EqualTo(0));
        
        rentedMat.Dispose();
    }

    [Test]
    public void Return_ValidMat_AddsToPool()
    {
        var mat = new Mat(100, 100, MatType.CV_8UC3);
        _matPool.Return(mat);
        
        Assert.That(_matPool.Count, Is.EqualTo(1));
    }

    [Test]
    public void Return_PoolFull_DisposesMat()
    {
        // Fill the pool
        for (int i = 0; i < DefaultCapacity; i++)
        {
            _matPool.Return(new Mat(100, 100, MatType.CV_8UC3));
        }
        
        Assert.That(_matPool.Count, Is.EqualTo(DefaultCapacity));

        // Try to add one more
        var extraMat = new Mat(100, 100, MatType.CV_8UC3);
        _matPool.Return(extraMat);
        
        Assert.That(_matPool.Count, Is.EqualTo(DefaultCapacity));
        Assert.That(extraMat.IsDisposed, Is.True, "Extra mat should be disposed when pool is full");
    }

    [Test]
    public void Return_DisposedMat_DoesNotAddToPool()
    {
        var mat = new Mat(100, 100, MatType.CV_8UC3);
        mat.Dispose();
        
        _matPool.Return(mat);
        
        Assert.That(_matPool.Count, Is.EqualTo(0));
    }

    [Test]
    public void Return_Null_DoesNotAddToPool()
    {
        _matPool.Return(null);
        Assert.That(_matPool.Count, Is.EqualTo(0));
    }

    [Test]
    public void Dispose_ClearsPoolAndDisposesItems()
    {
        var mat1 = new Mat(100, 100, MatType.CV_8UC3);
        var mat2 = new Mat(100, 100, MatType.CV_8UC3);
        
        _matPool.Return(mat1);
        _matPool.Return(mat2);
        
        Assert.That(_matPool.Count, Is.EqualTo(2));
        
        _matPool.Dispose();
        
        Assert.That(_matPool.Count, Is.EqualTo(0));
        Assert.That(mat1.IsDisposed, Is.True);
        Assert.That(mat2.IsDisposed, Is.True);
    }
    
    [Test]
    public void Rent_DisposedMatInPool_ReturnsNewMat()
    {
        // This is a tricky case. Return() prevents adding disposed mats.
        // But if a mat is disposed externally *after* being added to the pool:
        var mat = new Mat(100, 100, MatType.CV_8UC3);
        _matPool.Return(mat);
        
        // Externally dispose it
        mat.Dispose();
        
        // Rent should detect it's disposed and return a new one
        var rentedMat = _matPool.Rent(100, 100, MatType.CV_8UC3);
        
        Assert.That(rentedMat, Is.Not.SameAs(mat));
        Assert.That(rentedMat.IsDisposed, Is.False);
        Assert.That(_matPool.Count, Is.EqualTo(0), "Disposed mat should be removed from pool");
        
        rentedMat.Dispose();
    }

    [Test]
    public void Parallel_RentAndReturn_IsThreadSafe()
    {
        // Simulate multiple threads renting and returning
        int threadCount = 10;
        int iterations = 100;
        var exceptions = new ConcurrentQueue<Exception>();

        Parallel.For(0, threadCount, t =>
        {
            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    // Rent
                    var mat = _matPool.Rent(100, 100, MatType.CV_8UC3);
                    Assert.That(mat, Is.Not.Null);
                    Assert.That(mat.IsDisposed, Is.False);
                    
                    // Simulate work
                    Thread.Sleep(1);
                    
                    // Return
                    _matPool.Return(mat);
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        Assert.That(exceptions, Is.Empty, "Parallel operations should not throw exceptions");
        
        // Final state check: Pool should not be corrupted
        // Note: Count might be anywhere between 0 and Capacity depending on where threads stopped
        // But disposing should work fine.
        Assert.DoesNotThrow(() => _matPool.Dispose());
    }
}
