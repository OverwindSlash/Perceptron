using OpenCvSharp;

namespace Perceptron.Domain.DataStructure;

public interface IMatPool : IDisposable
{
    /// <summary>
    /// Rents a Mat from the pool with the specified dimensions and type.
    /// If no suitable Mat is available, a new one is created.
    /// </summary>
    Mat Rent(int rows, int cols, MatType type);

    /// <summary>
    /// Returns a Mat to the pool.
    /// </summary>
    void Return(Mat mat);

    /// <summary>
    /// Current number of items in the pool.
    /// </summary>
    int Count { get; }
}