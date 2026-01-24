using OpenCvSharp;
using System.Collections.Concurrent;

namespace Perceptron.Domain.DataStructure;

public class MatPool : IMatPool
{
    private readonly ConcurrentBag<Mat> _pool = new();
    private readonly int _maxCapacity;
    
    public int Count => _pool.Count;

    public MatPool(int maxCapacity = 100)
    {
        _maxCapacity = maxCapacity;
    }

    public Mat Rent(int rows, int cols, MatType type)
    {
        if (_pool.TryTake(out var mat))
        {
            // Ensure the mat is not disposed
            if (mat.IsDisposed)
            {
                 return new Mat(rows, cols, type);
            }

            // Check if mat matches requirements
            // Note: Mat.Create() or Resize() could be used, but strict matching is safer for now
            // to avoid reallocation overhead if dimensions change frequently.
            if (mat.Rows == rows && mat.Cols == cols && mat.Type() == type)
            {
                return mat; // Mat data is not cleared, caller should overwrite it.
            }
            else
            {
                // Wrong size/type, dispose and create new.
                // In a more advanced pool, we could bucket by size.
                mat.Dispose();
            }
        }

        return new Mat(rows, cols, type);
    }

    public void Return(Mat mat)
    {
        if (mat == null || mat.IsDisposed) return;
        
        if (_pool.Count >= _maxCapacity)
        {
            mat.Dispose();
            return;
        }

        _pool.Add(mat);
    }

    public void Dispose()
    {
        while (_pool.TryTake(out var mat))
        {
            mat?.Dispose();
        }
    }
}
