using OpenCvSharp;
using Perceptron.Domain.Entity.Common;
using System.Text.Json.Serialization;

namespace Perceptron.Domain.Entity.ObjectDetection
{
    /// <summary>
    /// 视频分析流水线中的“检测/跟踪目标实体”。
    /// </summary>
    public class DetectedObject : PropertiesBag, IDisposable
    {
        private readonly object _sync = new();

        private Mat? _snapshot;
        private int _isUnderAnalysis; // 0: false, 1: true
        private bool _isFrozen;

        private int _labelId;
        private string _label;
        private int _trackingId;

        // 4.1 核心字段
        public string SourceId { get; }
        public long FrameId { get; }
        public DateTime UtcTimeStamp { get; }
        
        // 可变字段 (构建阶段可写；冻结后不可写)
        public int LabelId 
        { 
            get => _labelId; 
            set 
            {
                if (_isFrozen) throw new InvalidOperationException("Object is frozen.");
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "LabelId must be >= 0.");
                _labelId = value;
            }
        }

        public string Label 
        { 
            get => _label; 
            set 
            {
                if (_isFrozen) throw new InvalidOperationException("Object is frozen.");
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Label cannot be empty or whitespace.", nameof(value));
                _label = value;
            }
        }

        public int TrackingId 
        { 
            get => _trackingId; 
            set 
            {
                if (_isFrozen) throw new InvalidOperationException("Object is frozen.");
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "TrackingId must be >= 0.");
                _trackingId = value;
            }
        }

        public float Confidence { get; }
        public BoundingBox Bbox { get; }

        public bool IsUnderAnalysis
        {
            get => Volatile.Read(ref _isUnderAnalysis) == 1;
            set => Volatile.Write(ref _isUnderAnalysis, value ? 1 : 0);
        }

        // 4.2 坐标与几何便捷属性（只读，序列化忽略）
        [JsonIgnore] public int X => Bbox.X;
        [JsonIgnore] public int Y => Bbox.Y;
        [JsonIgnore] public int Width => Bbox.Width;
        [JsonIgnore] public int Height => Bbox.Height;
        [JsonIgnore] public float CenterX => Bbox.CenterX;
        [JsonIgnore] public float CenterY => Bbox.CenterY;

        // 5.1 标识与键
        [JsonIgnore]
        public string DetectionKey => $"{SourceId}|{FrameId}|{LabelId}|{Bbox.X},{Bbox.Y},{Bbox.Width},{Bbox.Height}";

        [JsonIgnore]
        public string TrackKey => $"{SourceId}|{TrackingId}";

        [JsonIgnore]
        public string Id => $"{SourceId}_{Label}_{TrackingId}";

        [JsonIgnore]
        public string LocalId => $"{Label}_{TrackingId}";

        // 6.1 快照
        [JsonIgnore]
        public Mat? Snapshot
        {
            get
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    return _snapshot;
                }
            }
        }

        public bool HasSnapshot
        {
            get
            {
                lock (_sync)
                {
                    return _snapshot != null && !_snapshot.IsDisposed;
                }
            }
        }

        public bool IsDisposed { get; private set; }

        public DetectedObject(string sourceId, long frameId, DateTime utcTimeStamp, int labelId, string label, float confidence, BoundingBox bbox, int trackingId = 0)
        {
            // 12.1 构造/赋值校验
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("SourceId cannot be null or whitespace.", nameof(sourceId));
            if (frameId < 0) throw new ArgumentOutOfRangeException(nameof(frameId), "FrameId must be >= 0.");
            if (utcTimeStamp.Kind != DateTimeKind.Utc) throw new ArgumentException("UtcTimeStamp must be UTC.", nameof(utcTimeStamp));
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Label cannot be empty or whitespace.", nameof(label));
            if (labelId < 0) throw new ArgumentOutOfRangeException(nameof(labelId), "LabelId must be >= 0.");
            if (float.IsNaN(confidence) || float.IsInfinity(confidence) || confidence < 0 || confidence > 1)
                throw new ArgumentOutOfRangeException(nameof(confidence), $"Confidence must be within [0,1]. Actual={confidence}");
            if (bbox.IsEmpty) throw new ArgumentException("Bbox must have positive width and height.", nameof(bbox));
            if (trackingId < 0) throw new ArgumentOutOfRangeException(nameof(trackingId), "TrackingId must be >= 0.");

            SourceId = sourceId;
            FrameId = frameId;
            UtcTimeStamp = utcTimeStamp;
            _labelId = labelId;
            _label = label;
            Confidence = confidence;
            Bbox = bbox;
            _trackingId = trackingId;
        }

        // 6.2 快照管理
        public void AttachSnapshot(Mat snapshot, bool takeOwnership = true)
        {
            if (_isFrozen) throw new InvalidOperationException("Object is frozen.");
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Empty()) throw new ArgumentException("Snapshot is empty.", nameof(snapshot));

            lock (_sync)
            {
                ThrowIfDisposed();

                // 释放旧快照
                if (_snapshot != null)
                {
                    _snapshot.Dispose();
                    _snapshot = null;
                }

                if (takeOwnership)
                {
                    _snapshot = snapshot;
                }
                else
                {
                    _snapshot = snapshot.Clone();
                }
            }
        }

        public void DetachSnapshot()
        {
            if (_isFrozen) throw new InvalidOperationException("Object is frozen.");
            lock (_sync)
            {
                if (IsDisposed) return; // 允许幂等返回

                if (_snapshot != null)
                {
                    _snapshot.Dispose();
                    _snapshot = null;
                }
            }
        }

        public Mat? CloneSnapshot()
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                if (_snapshot == null || _snapshot.IsDisposed) return null;
                return _snapshot.Clone();
            }
        }

        // 7. 属性系统由基类 PropertiesBag 提供
        public override void SetProperty(string key, object? value)
        {
            if (_isFrozen) throw new InvalidOperationException("Object is frozen.");
            base.SetProperty(key, value);
        }

        public void Freeze()
        {
            _isFrozen = true;
        }

        public bool IsFrozen => _isFrozen;

        // 8. 几何与关系计算
        public float CalculateOverlapArea(DetectedObject other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return Bbox.IntersectionArea(other.Bbox);
        }

        public float CalculateIoU(DetectedObject other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return Bbox.IoU(other.Bbox);
        }

        public bool OverlapsWith(DetectedObject other, float threshold = 0.0f)
        {
            if (threshold < 0 || threshold > 1) throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be in [0,1].");
            return CalculateIoU(other) > threshold;
        }

        // 9.1 校验
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(SourceId) &&
                   FrameId >= 0 &&
                   UtcTimeStamp.Kind == DateTimeKind.Utc &&
                   LabelId >= 0 &&
                   !string.IsNullOrWhiteSpace(Label) &&
                   Confidence >= 0 && Confidence <= 1 && !float.IsNaN(Confidence) && !float.IsInfinity(Confidence) &&
                   !Bbox.IsEmpty &&
                   TrackingId >= 0;
        }

        // 9.2 ToString
        public override string ToString()
        {
            return $"{Id} src={SourceId} frame={FrameId} t={UtcTimeStamp:O} label={Label}({LabelId}) conf={Confidence:F3} bbox={Bbox} track={TrackingId}";
        }

        public void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(DetectedObject));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (IsDisposed) return;
                IsDisposed = true;

                if (_snapshot != null)
                {
                    _snapshot.Dispose();
                    _snapshot = null;
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
