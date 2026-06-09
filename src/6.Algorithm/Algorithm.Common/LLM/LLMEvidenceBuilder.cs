using OpenCvSharp;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using System.Text.Json;

namespace Algorithm.Common.LLM;

public static class LLMEvidenceBuilder
{
    public const int DefaultFrameJpegQuality = 80;
    public const int DefaultObjectCropJpegQuality = 85;
    public const double DefaultObjectCropPaddingRatio = 0.10;

    public static bool TryBuildFrameJpeg(Frame frame, int jpegQuality, out byte[] imageBytes)
    {
        imageBytes = [];

        try
        {
            var analysisImageJpeg = frame.GetProperty<byte[]>(LLMPropertyNames.AnalysisImageJpeg);
            if (analysisImageJpeg is { Length: > 0 })
            {
                imageBytes = analysisImageJpeg;
                return true;
            }

            frame.ThrowIfDisposed();
            if (frame.Scene.Empty())
            {
                return false;
            }

            return TryEncodeJpeg(frame.Scene, jpegQuality, out imageBytes);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public static bool TryBuildObjectCropJpeg(
        Frame frame,
        DetectedObject detectedObject,
        int jpegQuality,
        double paddingRatio,
        out byte[] imageBytes)
    {
        imageBytes = [];

        try
        {
            using var snapshot = detectedObject.CloneSnapshot();
            if (snapshot != null && !snapshot.Empty())
            {
                return TryEncodeJpeg(snapshot, jpegQuality, out imageBytes);
            }

            frame.ThrowIfDisposed();
            var cropRect = BuildPaddedRect(detectedObject, frame.Scene.Width, frame.Scene.Height, paddingRatio);
            if (cropRect.Width <= 0 || cropRect.Height <= 0)
            {
                return false;
            }

            using var crop = new Mat(frame.Scene, cropRect).Clone();
            return TryEncodeJpeg(crop, jpegQuality, out imageBytes);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (OpenCVException)
        {
            return false;
        }
    }

    public static FrameEvidence CreateFrameEvidence(Frame frame, byte[] frameJpeg)
    {
        var objects = frame.DetectedObjects
            .Select(DetectedObjectEvidence.FromDetectedObject)
            .ToList();

        string? annotationJson = null;
        try
        {
            annotationJson = JsonSerializer.Serialize(frame.Annotation);
        }
        catch (NotSupportedException)
        {
            annotationJson = null;
        }

        return new FrameEvidence(
            frame.SourceId,
            frame.FrameId,
            frame.OffsetMilliSec,
            frame.UtcTimeStamp,
            frameJpeg,
            objects,
            annotationJson);
    }

    public static double CalculateObjectEvidenceQuality(DetectedObject detectedObject, int frameWidth, int frameHeight)
    {
        var frameArea = Math.Max(1.0, frameWidth * frameHeight);
        var bboxAreaRatio = Math.Clamp((detectedObject.Width * detectedObject.Height) / frameArea, 0.0, 1.0);
        var centerX = frameWidth <= 0 ? 0.5 : detectedObject.CenterX / frameWidth;
        var centerY = frameHeight <= 0 ? 0.5 : detectedObject.CenterY / frameHeight;
        var distanceFromCenter = Math.Sqrt(Math.Pow(centerX - 0.5, 2) + Math.Pow(centerY - 0.5, 2));
        var centerScore = Math.Clamp(1.0 - distanceFromCenter * 2.0, 0.0, 1.0);

        return detectedObject.Confidence * 0.60 + bboxAreaRatio * 0.25 + centerScore * 0.15;
    }

    private static bool TryEncodeJpeg(Mat image, int jpegQuality, out byte[] imageBytes)
    {
        imageBytes = [];
        var quality = Math.Clamp(jpegQuality, 1, 100);
        var parameters = new ImageEncodingParam(ImwriteFlags.JpegQuality, quality);
        Cv2.ImEncode(".jpg", image, out imageBytes, parameters);
        return imageBytes.Length > 0;
    }

    private static Rect BuildPaddedRect(DetectedObject detectedObject, int frameWidth, int frameHeight, double paddingRatio)
    {
        var paddingX = (int)Math.Round(detectedObject.Width * Math.Max(0, paddingRatio));
        var paddingY = (int)Math.Round(detectedObject.Height * Math.Max(0, paddingRatio));

        var x = Math.Max(0, detectedObject.X - paddingX);
        var y = Math.Max(0, detectedObject.Y - paddingY);
        var right = Math.Min(frameWidth, detectedObject.X + detectedObject.Width + paddingX);
        var bottom = Math.Min(frameHeight, detectedObject.Y + detectedObject.Height + paddingY);

        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
