针对“分析窗口内共享 Frame”的场景（例如 300 帧滑动窗口 + 多算法并行分析），简单的“用完即还”的池化策略会导致严重的内存安全问题（如：算法A正在读取 Frame 100，而 Frame 100 已经被窗口移除并回收到池中，导致数据损坏或崩溃）。

针对这一挑战，最佳实践是采用 **引用计数（Reference Counting）** 与 **资源池化（Resource Pooling）** 相结合的方案。

### 核心设计思路

1.  **资源池化 (MatPool)**：由于 `Frame` 对象本身很轻量，真正占用大量内存且分配昂贵的是 OpenCV 的 `Mat` 对象。因此，我们**只池化 `Mat`**，而不是 `Frame` 包装器。
2.  **引用计数 (RefCounting)**：在 `Frame` 中引入引用计数机制。只有当所有消费者（VideoLoader、滑动窗口、分析算法、UI显示）都明确表示“用完了”（调用 Dispose），引用计数归零时，才真正将底层的 `Mat` 归还给池。

简而言之：引用计数在 `Frame` 层面管理生命周期，当 `Frame` 引用计数等于0，执行真正的 Dispose ，将 `Mat` 还回 `MatPool` 。

---

### 代码实现

我已经为你完成了 `Frame` 类的改造，并新增了 `MatPool` 实现。

#### 1. 改造 `Frame.cs` 支持引用计数

我在 [Frame.cs](file:///Users/overwind/Workspace/Formal/Insight/src/3.Domain/Insight.Domain/Entity/VideoStream/Frame.cs) 中增加了 `Retain()` 方法和内部计数器：

```csharp
// Frame.cs 关键变更
public class Frame : PropertiesBag, IDisposable
{
    private int _refCount = 1; // 初始引用为 1 (创建者持有)
    private readonly Action<Mat>? _recycler; // 回收回调

    // 构造函数注入回收器
    public Frame(..., Action<Mat>? recycler = null) { ... }

    // 增加引用计数（算法或窗口在持有 Frame 前调用）
    public void Retain()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _refCount);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        
        // 减少引用计数，只有归零时才真正释放
        if (Interlocked.Decrement(ref _refCount) > 0) return;

        // 归零：将 Mat 归还给池（如果提供了回收器），否则销毁
        if (_recycler != null) _recycler(Scene);
        else Scene?.Dispose();
        
        // ... 清理其他资源
    }
}
```

#### 2. 新增 `MatPool` 实现

在 [MatPool.cs](file:///Users/overwind/Workspace/Formal/Insight/src/3.Domain/Insight.Domain/DataStructure/MatPool.cs) 中实现了线程安全的 Mat 池：

```csharp
public class MatPool : IMatPool
{
    private readonly ConcurrentBag<Mat> _pool = new();
    
    // 借出 Mat：优先复用尺寸/类型匹配的
    public Mat Rent(int rows, int cols, MatType type) { ... }
    
    // 归还 Mat
    public void Return(Mat mat) { ... }
}
```

---

### 最佳实践：如何在管线中使用

在你的 300 帧分析窗口场景中，正确的使用流程如下：

1.  **加载器生产 (Producer)**：
    ```csharp
    // VideoLoader 从池中借出 Mat，创建 Frame
    var mat = _matPool.Rent(1080, 1920, MatType.CV_8UC3);
    // 传入回收委托：当 Frame 彻底销毁时，把 mat 还给 _matPool
    var frame = new Frame(..., mat, (m) => _matPool.Return(m));
    // 此时 frame.RefCount = 1
    ```

2.  **窗口管理 (Window Buffer)**：
    ```csharp
    // 当 Frame 进入滑动窗口
    windowQueue.Enqueue(frame);
    // 窗口持有 Frame，不需要 Retain，因为 Loader 移交了所有权
    // 或者为了清晰，Loader 调用 Retain，窗口 Dispose，但这增加开销。
    // 通常约定：Pipeline 传递意味着所有权转移。
    ```

3.  **算法分析 (Consumer)**：
    假设有一个异步算法需要处理第 100 帧：
    ```csharp
    void AnalyzeAsync(Frame frame)
    {
        // 【关键】算法开始异步处理，必须 Retain，防止窗口滑出导致 Frame 被回收
        frame.Retain(); 
        
        Task.Run(() => {
            try {
                // ... 漫长的分析过程 ...
                // 即使此时窗口已经滑过并 Dispose 了该帧，
                // 由于 RefCount > 0，Mat 依然有效，内存安全。
                Process(frame.Scene); 
            }
            finally {
                // 【关键】处理完毕，释放引用
                frame.Dispose(); 
            }
        });
    }
    ```

4.  **窗口滑出**：
    当窗口已满，移除旧帧：
    ```csharp
    if (windowQueue.Count > 300)
    {
        var oldFrame = windowQueue.Dequeue();
        oldFrame.Dispose(); 
        // 如果没有算法在用它 (RefCount=0)，Mat 立即回池。
        // 如果有算法在用 (RefCount>0)，Mat 保持存活，直到算法也 Dispose。
    }
    ```

这种方案完美解决了**生命周期管理**与**高性能复用**的冲突，确保了在复杂的并行分析管线中既不会发生内存泄漏，也不会发生 Use-After-Free 错误。