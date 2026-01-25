using OpenCvSharp;

namespace Detector.Common;

public static class YoloPredictionMerger
{
    public static List<YoloPrediction> MergeFromTiles(
        IEnumerable<ImageTile> tiles,
        MergeOptions? options = null)
    {
        options ??= new MergeOptions();

        var tileList = tiles.ToList();
        if (tileList.Count == 0) return new List<YoloPrediction>();

        // 计算网格规模与切缝位置
        int rows = tileList.Max(t => t.RowIndex) + 1;
        int cols = tileList.Max(t => t.ColIndex) + 1;

        // 这里假设规则等分：以“出现次数最多”的 tile 尺寸为基准
        int tileW = tileList.GroupBy(t => t.TileWidth).OrderByDescending(g => g.Count()).First().Key;
        int tileH = tileList.GroupBy(t => t.TileHeight).OrderByDescending(g => g.Count()).First().Key;

        // 垂直切缝（x = k * tileW, k=1..cols-1），水平切缝（y = k * tileH, k=1..rows-1）
        var vSeams = Enumerable.Range(1, Math.Max(0, cols - 1)).Select(k => k * tileW).ToArray();
        var hSeams = Enumerable.Range(1, Math.Max(0, rows - 1)).Select(k => k * tileH).ToArray();

        // 1) 投影到原图坐标
        var projected = new List<YoloPrediction>();
        foreach (var t in tileList)
        {
            int xOffset = t.ColIndex * tileW;
            int yOffset = t.RowIndex * tileH;

            foreach (var p in t.Predictions)
                projected.Add(ToGlobal(p, xOffset, yOffset));
        }

        if (projected.Count <= 1) return projected.ToList();

        // 2) 仅按“贴缝”关系建图聚类（不做 IoU 普通重叠合并）
        var groups = ClusterByMergePredicate(projected, (a, b) => ShouldStitchAcrossSeam(a, b, vSeams, hSeams, options));

        // 3) 聚合每个连通分量
        return groups.Select(g => AggregateGroup(g, options)).ToList();
    }

    private static YoloPrediction ToGlobal(YoloPrediction p, int xOffset, int yOffset)
    {
        return new YoloPrediction
        {
            TypeId = p.TypeId,
            Type = p.Type,
            Confidence = p.Confidence,
            TrackingId = p.TrackingId,
            BBox = new Rect(p.X + xOffset, p.Y + yOffset, p.Width, p.Height)
        };
    }

    private static List<List<YoloPrediction>> ClusterByMergePredicate(
        List<YoloPrediction> items,
        Func<YoloPrediction, YoloPrediction, bool> shouldMerge)
    {
        int n = items.Count;
        var visited = new bool[n];
        var result = new List<List<YoloPrediction>>();

        for (int i = 0; i < n; i++)
        {
            if (visited[i]) continue;
            var queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            var group = new List<YoloPrediction> { items[i] };

            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                for (int v = 0; v < n; v++)
                {
                    if (visited[v]) continue;
                    if (shouldMerge(items[u], items[v]))
                    {
                        visited[v] = true;
                        queue.Enqueue(v);
                        group.Add(items[v]);
                    }
                }
            }
            result.Add(group);
        }

        return result;
    }

    private static bool ShouldStitchAcrossSeam(
        YoloPrediction a,
        YoloPrediction b,
        int[] verticalSeams,
        int[] horizontalSeams,
        MergeOptions opt)
    {
        if (opt.RequireSameClass)
        {
            if (a.TypeId != b.TypeId && !string.Equals(a.Type, b.Type, StringComparison.Ordinal))
                return false;
        }

        // 横向贴缝（跨垂直切缝）
        foreach (var s in verticalSeams)
        {
            if (AreHorizontallySeamStitchable(a.BBox, b.BBox, s, opt.MaxStitchGapPx, opt.MinOrthOverlapRatio))
                return true;
        }

        // 纵向贴缝（跨水平切缝）
        foreach (var s in horizontalSeams)
        {
            if (AreVerticallySeamStitchable(a.BBox, b.BBox, s, opt.MaxStitchGapPx, opt.MinOrthOverlapRatio))
                return true;
        }

        return false;
    }

    private static bool AreHorizontallySeamStitchable(Rect A, Rect B, int seamX, int maxGap, float minOverlapRatio)
    {
        // 要求 A/B 分别在 seamX 左右两侧
        bool aLeft = A.Right <= seamX;
        bool bRight = B.X >= seamX;
        bool bLeft = B.Right <= seamX;
        bool aRight = A.X >= seamX;

        // 情况1：A在左，B在右
        if (aLeft && bRight)
        {
            int gap = B.X - A.Right; // >= 0
            if (gap <= maxGap && OrthOverlapY(A, B) >= minOverlapRatio)
                return true;
        }
        // 情况2：B在左，A在右
        if (bLeft && aRight)
        {
            int gap = A.X - B.Right; // >= 0
            if (gap <= maxGap && OrthOverlapY(A, B) >= minOverlapRatio)
                return true;
        }
        return false;
    }

    private static bool AreVerticallySeamStitchable(Rect A, Rect B, int seamY, int maxGap, float minOverlapRatio)
    {
        // 要求 A/B 分别在 seamY 上下两侧
        bool aTop = A.Bottom <= seamY;
        bool bBottom = B.Y >= seamY;
        bool bTop = B.Bottom <= seamY;
        bool aBottom = A.Y >= seamY;

        // 情况1：A在上，B在下
        if (aTop && bBottom)
        {
            int gap = B.Y - A.Bottom; // >= 0
            if (gap <= maxGap && OrthOverlapX(A, B) >= minOverlapRatio)
                return true;
        }
        // 情况2：B在上，A在下
        if (bTop && aBottom)
        {
            int gap = A.Y - B.Bottom; // >= 0
            if (gap <= maxGap && OrthOverlapX(A, B) >= minOverlapRatio)
                return true;
        }
        return false;
    }

    private static float OrthOverlapY(Rect A, Rect B)
    {
        int overlap = Overlap1D(A.Y, A.Bottom, B.Y, B.Bottom);
        int minH = Math.Min(A.Height, B.Height);
        if (minH <= 0) return 0f;
        return (float)overlap / minH;
    }

    private static float OrthOverlapX(Rect A, Rect B)
    {
        int overlap = Overlap1D(A.X, A.Right, B.X, B.Right);
        int minW = Math.Min(A.Width, B.Width);
        if (minW <= 0) return 0f;
        return (float)overlap / minW;
    }

    private static int Overlap1D(int a1, int a2, int b1, int b2)
    {
        int left = Math.Max(a1, b1);
        int right = Math.Min(a2, b2);
        return Math.Max(0, right - left);
    }

    private static YoloPrediction AggregateGroup(List<YoloPrediction> group, MergeOptions options)
    {
        var byClass = group.GroupBy(g => (g.TypeId, g.Type))
                           .OrderByDescending(g => g.Count())
                           .ThenByDescending(g => g.Select(x => x.BBox.Area()).Sum())
                           .First();

        float confidence = options.ConfidenceMode switch
        {
            ConfidenceAggregateMode.Max => group.Max(x => x.Confidence),
            ConfidenceAggregateMode.Average => (float)group.Average(x => x.Confidence),
            _ => group.Max(x => x.Confidence)
        };

        var unionRect = UnionRect(byClass.Select(x => x.BBox));

        int trackingId = options.TrackingMode switch
        {
            TrackingIdAggregateMode.MinNonZero => group.Where(x => x.TrackingId > 0).Select(x => x.TrackingId).DefaultIfEmpty(0).Min(),
            TrackingIdAggregateMode.Zero => 0,
            _ => 0
        };

        return new YoloPrediction
        {
            TypeId = byClass.Key.TypeId,
            Type = byClass.Key.Type,
            Confidence = confidence,
            TrackingId = trackingId,
            BBox = unionRect
        };
    }

    private static Rect UnionRect(IEnumerable<Rect> rects)
    {
        using var e = rects.GetEnumerator();
        if (!e.MoveNext()) return new Rect();

        int minX = e.Current.X, minY = e.Current.Y, maxX = e.Current.Right, maxY = e.Current.Bottom;
        while (e.MoveNext())
        {
            var r = e.Current;
            if (r.X < minX) minX = r.X;
            if (r.Y < minY) minY = r.Y;
            if (r.Right > maxX) maxX = r.Right;
            if (r.Bottom > maxY) maxY = r.Bottom;
        }
        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private static int Area(this Rect r) => r.Width * r.Height;
}