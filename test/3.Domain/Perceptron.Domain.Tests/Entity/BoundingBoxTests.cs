using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;

namespace Perceptron.Domain.Tests.Entity;

[TestFixture]
public class BoundingBoxTests
{
    [Test]
    public void Default_Is_Empty()
    {
        BoundingBox box = default;
        Assert.That(box.IsEmpty, Is.True);
        Assert.That(box.Width, Is.EqualTo(0));
        Assert.That(box.Height, Is.EqualTo(0));
        Assert.That(box.X, Is.EqualTo(0));
        Assert.That(box.Y, Is.EqualTo(0));
    }

    [Test]
    public void CreateFromRect_Validates_Dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundingBox.CreateFromRect(0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundingBox.CreateFromRect(0, 0, 10, -1));
            
        var box = BoundingBox.CreateFromRect(10, 20, 30, 40);
        Assert.That(box.X, Is.EqualTo(10));
        Assert.That(box.Y, Is.EqualTo(20));
        Assert.That(box.Width, Is.EqualTo(30));
        Assert.That(box.Height, Is.EqualTo(40));
    }

    [Test]
    public void CreateFromFourPoints_Sorts_Coordinates()
    {
        var box = BoundingBox.CreateFromFourPoints(100, 100, 10, 20);
        Assert.That(box.X, Is.EqualTo(10));
        Assert.That(box.Y, Is.EqualTo(20));
        Assert.That(box.Width, Is.EqualTo(90)); // 100 - 10
        Assert.That(box.Height, Is.EqualTo(80)); // 100 - 20
    }

    [Test]
    public void CreateFromYolo_Calculates_TopLeft()
    {
        // Center (50, 50), W=20, H=20 -> X=40, Y=40
        var box = BoundingBox.CreateFromYolo(50, 50, 20, 20);
        Assert.That(box.X, Is.EqualTo(40));
        Assert.That(box.Y, Is.EqualTo(40));
        Assert.That(box.Width, Is.EqualTo(20));
        Assert.That(box.Height, Is.EqualTo(20));
            
        // Check rounding (integer division truncation)
        // Center (50, 50), W=21. 21/2 = 10. X = 50 - 10 = 40.
        var boxOdd = BoundingBox.CreateFromYolo(50, 50, 21, 21);
        Assert.That(boxOdd.X, Is.EqualTo(40));
    }

    [Test]
    public void Contains_Respects_LeftClosedRightOpen()
    {
        var box = BoundingBox.CreateFromRect(10, 10, 10, 10); // [10, 20)
            
        Assert.That(box.Contains(10, 10), Is.True);
        Assert.That(box.Contains(19, 19), Is.True);
        Assert.That(box.Contains(20, 10), Is.False, "Right edge exclusive");
        Assert.That(box.Contains(10, 20), Is.False, "Bottom edge exclusive");
    }

    [Test]
    public void IntersectsWith_Touching_Returns_False()
    {
        var box1 = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var box2 = BoundingBox.CreateFromRect(10, 0, 10, 10); // Touches at X=10
            
        Assert.That(box1.IntersectsWith(box2), Is.False);
        Assert.That(box1.IntersectionArea(box2), Is.EqualTo(0));
    }

    [Test]
    public void TryIntersection_NoIntersection_Returns_False_And_Empty()
    {
        var box1 = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var box2 = BoundingBox.CreateFromRect(20, 20, 10, 10);
            
        bool result = box1.TryIntersection(box2, out var intersection);
        Assert.That(result, Is.False);
        Assert.That(intersection.IsEmpty, Is.True);
    }

    [Test]
    public void IoU_Handles_NoIntersection()
    {
        var box1 = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var box2 = BoundingBox.CreateFromRect(20, 20, 10, 10);
            
        Assert.That(box1.IoU(box2), Is.EqualTo(0));
    }
        
    [Test]
    public void IoU_Handles_Empty()
    {
        var box1 = BoundingBox.CreateFromRect(0, 0, 10, 10);
        Assert.That(box1.IoU(BoundingBox.Empty), Is.EqualTo(0));
    }

    [Test]
    public void ClipTo_Validates_Bounds()
    {
        var box = BoundingBox.CreateFromRect(-10, -10, 50, 50);
            
        // Clip to 100x100 image
        var clipped = box.TryClipTo(100, 100);
        Assert.That(clipped, Is.Not.Null);
        Assert.That(clipped.Value.X, Is.EqualTo(0));
        Assert.That(clipped.Value.Y, Is.EqualTo(0));
        Assert.That(clipped.Value.Width, Is.EqualTo(40)); // 50 - 10
        Assert.That(clipped.Value.Height, Is.EqualTo(40));
            
        // Completely outside
        var outside = BoundingBox.CreateFromRect(-100, -100, 10, 10);
        Assert.That(outside.TryClipTo(100, 100), Is.Null);
        Assert.Throws<InvalidOperationException>(() => outside.ClipTo(100, 100));
    }

    [Test]
    public void Overflow_Protection()
    {
        // Max int X, positive width -> Overflow
        // But we can't create such a box because X+Width must be checked in properties?
        // Spec says "checked(X + Width)".
        // Let's try to access XMax with large values.
            
        var box = BoundingBox.CreateFromRect(int.MaxValue - 10, 0, 20, 10);
        Assert.Throws<OverflowException>(() => { var x = box.XMax; });
    }

    [Test]
    public void Normalization_Validates_Inputs()
    {
        Assert.Throws<ArgumentException>(() => BoundingBox.CreateFromYoloNormalized(float.NaN, 0.5f, 0.1f, 0.1f, 100, 100));
        Assert.Throws<ArgumentException>(() => BoundingBox.CreateFromYoloNormalized(0.5f, 0.5f, float.PositiveInfinity, 0.1f, 100, 100));
    }
        
    [Test]
    public void ScaleAboutCenter_Keeps_Center()
    {
        var box = BoundingBox.CreateFromRect(0, 0, 100, 100); // Center 50, 50
        var scaled = box.ScaleAboutCenter(2.0f, 2.0f); // Should be 200x200, Center 50, 50
            
        // New Width 200, Height 200.
        // New X = 50 - 200/2 = -50
        // New Y = 50 - 200/2 = -50
            
        Assert.That(scaled.Width, Is.EqualTo(200));
        Assert.That(scaled.Height, Is.EqualTo(200));
        Assert.That(scaled.X, Is.EqualTo(-50));
        Assert.That(scaled.Y, Is.EqualTo(-50));
        Assert.That(scaled.CenterX, Is.EqualTo(50));
        Assert.That(scaled.CenterY, Is.EqualTo(50));
    }

    [Test]
    public void ScaleAboutCenter_Handles_Small_Scale()
    {
        var box = BoundingBox.CreateFromRect(0, 0, 100, 100);
        var scaled = box.ScaleAboutCenter(0.001f, 0.001f);
             
        // Width -> 0.1 -> Rounds to 0? AwayFromZero -> 0.1 -> 0?
        // Math.Round(0.1) is 0.
        // If result is 0, we clamp to 1.
             
        Assert.That(scaled.Width, Is.GreaterThanOrEqualTo(1));
        Assert.That(scaled.Height, Is.GreaterThanOrEqualTo(1));
    }

    #region Missing Coverage Added

    [Test]
    public void CreateFromFourPoints_ZeroArea_Throws()
    {
        Assert.Throws<ArgumentException>(() => BoundingBox.CreateFromFourPoints(10, 10, 10, 20)); // Width 0
        Assert.Throws<ArgumentException>(() => BoundingBox.CreateFromFourPoints(10, 10, 20, 10)); // Height 0
    }

    [Test]
    public void UnionArea_CalculatesCorrectly()
    {
        var a = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var b = BoundingBox.CreateFromRect(5, 5, 10, 10);
        // Area A = 100, Area B = 100, Intersection = 25
        // Union = 100 + 100 - 25 = 175
        Assert.That(a.UnionArea(b), Is.EqualTo(175));
    }

    [Test]
    public void IoF_CalculatesCorrectly()
    {
        var large = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var small = BoundingBox.CreateFromRect(0, 0, 5, 5);
        
        // Intersection = 25. MinArea = 25. IoF = 1.
        Assert.That(large.IoF(small), Is.EqualTo(1.0f));
        Assert.That(small.IoF(large), Is.EqualTo(1.0f));
        
        // OverlapPercentage is Alias
        Assert.That(large.OverlapPercentage(small), Is.EqualTo(1.0f));
    }

    [Test]
    public void IoF_Handles_Empty()
    {
        var box1 = BoundingBox.CreateFromRect(0, 0, 10, 10);
        Assert.That(box1.IoF(BoundingBox.Empty), Is.EqualTo(0));
        Assert.That(BoundingBox.Empty.IoF(box1), Is.EqualTo(0));
    }

    [Test]
    public void GetIntersection_ReturnsNullable()
    {
        var a = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var b = BoundingBox.CreateFromRect(20, 20, 10, 10);
        var c = BoundingBox.CreateFromRect(5, 5, 10, 10);

        Assert.That(a.GetIntersection(b), Is.Null);
        
        var inter = a.GetIntersection(c);
        Assert.That(inter.HasValue, Is.True);
        Assert.That(inter!.Value.Width, Is.EqualTo(5));
    }

    [Test]
    public void Contains_BoundingBox()
    {
        var outer = BoundingBox.CreateFromRect(0, 0, 100, 100);
        var inner = BoundingBox.CreateFromRect(10, 10, 20, 20);
        var partial = BoundingBox.CreateFromRect(90, 90, 20, 20);

        Assert.That(outer.Contains(inner), Is.True);
        Assert.That(outer.Contains(partial), Is.False);
        Assert.That(outer.Contains(BoundingBox.Empty), Is.False);
    }

    [Test]
    public void CenterDistanceTo_Euclidean()
    {
        var a = BoundingBox.CreateFromRect(0, 0, 2, 2); // Center (1, 1)
        var b = BoundingBox.CreateFromRect(4, 5, 2, 2); // Center (5, 6)
        // dx = 4, dy = 5. Dist = sqrt(16+25) = sqrt(41) ≈ 6.403
        
        Assert.That(a.CenterDistanceTo(b), Is.EqualTo(MathF.Sqrt(41)).Within(1e-5));
    }

    [Test]
    public void MinDistance_CalculatesEdgeDistance()
    {
        // Touching
        var a = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var b = BoundingBox.CreateFromRect(10, 0, 10, 10);
        Assert.That(a.MinDistance(b), Is.EqualTo(0));

        // Separated Horizontally
        var c = BoundingBox.CreateFromRect(15, 0, 10, 10);
        Assert.That(a.MinDistance(c), Is.EqualTo(5));

        // Separated Diagonally
        var d = BoundingBox.CreateFromRect(13, 14, 10, 10);
        // X dist: 13 - 10 = 3. Y dist: 14 - 10 = 4.
        // Dist = 5.
        Assert.That(a.MinDistance(d), Is.EqualTo(5));
    }

    [Test]
    public void Merge_EnclosingBox()
    {
        var a = BoundingBox.CreateFromRect(0, 0, 10, 10);
        var b = BoundingBox.CreateFromRect(10, 10, 10, 10);
        
        var merged = a.Merge(b);
        Assert.That(merged.X, Is.EqualTo(0));
        Assert.That(merged.Y, Is.EqualTo(0));
        Assert.That(merged.Width, Is.EqualTo(20));
        Assert.That(merged.Height, Is.EqualTo(20));
    }

    [Test]
    public void Translate_MovesBox()
    {
        var box = BoundingBox.CreateFromRect(10, 10, 20, 20);
        var moved = box.Translate(5, -5);
        
        Assert.That(moved.X, Is.EqualTo(15));
        Assert.That(moved.Y, Is.EqualTo(5));
        Assert.That(moved.Width, Is.EqualTo(20));
    }

    [Test]
    public void Equality_And_ToString()
    {
        var a = BoundingBox.CreateFromRect(1, 2, 3, 4);
        var b = BoundingBox.CreateFromRect(1, 2, 3, 4);
        var c = BoundingBox.CreateFromRect(1, 2, 3, 5);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        Assert.That(a.ToString(), Does.Contain("X=1"));
    }

    [Test]
    public void Normalization_RoundTrip()
    {
        int w = 100, h = 200;
        var box = BoundingBox.CreateFromRect(10, 20, 30, 40);
        
        // 1. ToNormalized -> FromNormalized
        var norm = box.ToNormalized(w, h);
        var recovered = BoundingBox.FromNormalized(norm.X, norm.Y, norm.Width, norm.Height, w, h);
        
        Assert.That(recovered, Is.EqualTo(box));

        // 2. ToYoloNormalized -> CreateFromYoloNormalized
        var yolo = box.ToYoloNormalized(w, h);
        var recoveredYolo = BoundingBox.CreateFromYoloNormalized(yolo.cx, yolo.cy, yolo.w, yolo.h, w, h);
        
        // Note: Integer division/rounding might cause off-by-one errors in center calculation if not careful,
        // but our implementation uses consistent Rounding.
        Assert.That(recoveredYolo.X, Is.EqualTo(box.X).Within(1));
        Assert.That(recoveredYolo.Y, Is.EqualTo(box.Y).Within(1));
        Assert.That(recoveredYolo.Width, Is.EqualTo(box.Width).Within(1));
    }

    [Test]
    public void ScaleAboutCenter_RoundingAndClamp()
    {
        var a = BoundingBox.CreateFromRect(10, 10, 5, 5); // Center(12, 12) (float 12.5, 12.5)
        // Note: Spec says CenterX = X + Width/2f = 10 + 2.5 = 12.5
        //       CenterY = Y + Height/2f = 10 + 2.5 = 12.5
        
        var s = a.ScaleAboutCenter(1.5f, 2.0f); // NewW=8 (7.5->8), NewH=10
        // NewX = 12.5 - 4 = 8.5 -> 8
        // NewY = 12.5 - 5 = 7.5 -> 7
        
        Assert.Multiple(() =>
        {
            Assert.That(s.Width, Is.EqualTo(8));
            Assert.That(s.Height, Is.EqualTo(10));
            Assert.That(s.X, Is.EqualTo(8)); 
            Assert.That(s.Y, Is.EqualTo(7));
        });
    }

    [TestCase(0f, 1f)]
    [TestCase(1f, 0f)]
    [TestCase(-1f, 1f)]
    [TestCase(1f, -1f)]
    public void ScaleAboutCenter_Invalid_Throws(float sx, float sy)
    {
        var a = BoundingBox.CreateFromRect(5, 5, 10, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => a.ScaleAboutCenter(sx, sy));
    }
    
    [Test]
    public void Properties_CalculatedCorrectly()
    {
        var b = BoundingBox.CreateFromRect(10, 20, 30, 40);
        Assert.Multiple(() =>
        {
            Assert.That(b.XMin, Is.EqualTo(10));
            Assert.That(b.YMin, Is.EqualTo(20));
            Assert.That(b.XMax, Is.EqualTo(40)); // 10+30
            Assert.That(b.YMax, Is.EqualTo(60)); // 20+40
            Assert.That(b.CenterX, Is.EqualTo(25f)); // 10 + 15
            Assert.That(b.CenterY, Is.EqualTo(40f)); // 20 + 20
            Assert.That(b.Top, Is.EqualTo(20));
            Assert.That(b.Bottom, Is.EqualTo(60));
            Assert.That(b.Left, Is.EqualTo(10));
            Assert.That(b.Right, Is.EqualTo(40));
            Assert.That(b.Area, Is.EqualTo(1200));
            Assert.That(b.Rectangle, Is.EqualTo(new Rect(10, 20, 30, 40)));
        });
    }

    #endregion
}