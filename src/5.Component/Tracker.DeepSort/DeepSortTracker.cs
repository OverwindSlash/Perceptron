using ComponentCommon;
using Microsoft.ML.OnnxRuntime;
using Perceptron.Domain.Abstraction.ObjectTracker;
using Perceptron.Domain.Entity.VideoStream;
using System.Drawing;
using Tracker.DeepSort.Matchers.DeepSort;
using Tracker.DeepSort.ReID;
using Tracker.DeepSort.ReID.Models.Fast_Reid;
using Tracker.DeepSort.ReID.Models.OSNet;

namespace Tracker.DeepSort;

public class DeepSortTracker : ComponentBase, IObjectTracker, IDisposable
{
    private readonly DeepSortMatcher _matcher;
    private readonly IAppearanceExtractor? _appearanceExtractor;
    private readonly float _targetConfidence;

    /// <summary>
    /// 从偏好配置创建 DeepSortTracker。
    /// 支持偏好键（示例）:
    ///   "AppearanceExtractorVersion" : "0"
    ///   "AppearanceExtractorFilePath" : "Models/Reid/osnet_x1_0_msmt17.onnx"
    ///   "AppearanceWeight" : "0.8"
    ///   "SmoothAppearanceWeight" : "0.875"
    ///   "FramesToAppearanceSmooth" : "40"
    ///   "TargetConfidence" : "0.4"
    ///   "SessionDevice" : "cpu" 或 "cuda"  （默认为 cpu）
    ///   或者兼容旧键 "UseGpu" : "true"/"false"
    /// 必需：AppearanceExtractorFilePath（且文件存在）。目前仅支持 AppearanceExtractorVersion == 0（CustomReidModel）。
    /// </summary>
    public DeepSortTracker(Dictionary<string, string>? preferences) : base(preferences)
    {
        preferences ??= new Dictionary<string, string>();

        int appearanceVersion = ParseInt(preferences, "AppearanceExtractorVersion", 0);
        string? extractorPath = TryGet(preferences, "AppearanceExtractorFilePath");
        float appearanceWeight = ParseFloat(preferences, "AppearanceWeight", 0.8f);
        float smoothAppearanceWeight = ParseFloat(preferences, "SmoothAppearanceWeight", 0.875f);
        int framesToAppearanceSmooth = ParseInt(preferences, "FramesToAppearanceSmooth", 40);
        _targetConfidence = ParseFloat(preferences, "TargetConfidence", 0.4f);
        float iouThreshold = ParseFloat(preferences, "IouThreshold", 0.5f);
        int maxMisses = ParseInt(preferences, "MaxMisses", 50);

        // 新增：读取 Session 配置（支持 "SessionDevice"="cpu"|"cuda"，或 "UseGpu"="true"）
        string sessionDevice = "cpu";
        var sessionDevicePref = TryGet(preferences, "SessionDevice");
        if (!string.IsNullOrWhiteSpace(sessionDevicePref))
        {
            sessionDevice = sessionDevicePref!.Trim().ToLowerInvariant();
        }
        else
        {
            var useGpu = TryGet(preferences, "UseGpu");
            if (!string.IsNullOrWhiteSpace(useGpu) && bool.TryParse(useGpu, out var useGpuBool) && useGpuBool)
                sessionDevice = "cuda";
        }

        if (string.IsNullOrWhiteSpace(extractorPath))
            throw new InvalidOperationException("AppearanceExtractorFilePath must be provided in preferences to initialize DeepSortTracker.");

        if (!File.Exists(extractorPath))
            throw new FileNotFoundException($"Appearance extractor model file not found: {extractorPath}", extractorPath);

        if (appearanceVersion != 0)
            throw new NotSupportedException($"AppearanceExtractorVersion '{appearanceVersion}' is not supported.");

        // 直接根据参数构建外观提取器（不使用中间 MatcherOption）
        _appearanceExtractor = ConstructAppearanceExtractorFromOptions(appearanceVersion, extractorPath, sessionDevice);

        _matcher = new DeepSortMatcher(_appearanceExtractor,
            appearanceWeight: appearanceWeight,
            threshold: iouThreshold,
            maxMisses: maxMisses,
            framesToAppearanceSmooth: framesToAppearanceSmooth,
            smoothAppearanceWeight: smoothAppearanceWeight,
            minStreak: 8,
            poolCapacity: 50);
    }

    /// <summary>
    /// 直接注入外观提取器的构造函数（单元测试或外部创建提取器时使用）。 
    /// </summary>
    public DeepSortTracker(
        IAppearanceExtractor appearanceExtractor,
        float appearanceWeight = 0.775f,
        float threshold = 0.5f,
        int maxMisses = 50,
        int framesToAppearanceSmooth = 40,
        float smoothAppearanceWeight = 0.875f,
        int minStreak = 8,
        int poolCapacity = 50) : base(null)
    {
        _appearanceExtractor = appearanceExtractor ?? throw new ArgumentNullException(nameof(appearanceExtractor));
        _matcher = new DeepSortMatcher(_appearanceExtractor, appearanceWeight, threshold, maxMisses,
            framesToAppearanceSmooth, smoothAppearanceWeight, minStreak, poolCapacity);

        _targetConfidence = 0.4f;
    }

    /// <summary>
    /// 将 Frame 中的检测对象转为 DeepSort 的预测，调用匹配器并把轨迹 Id 回写到 DetectedObject.TrackingId。
    /// 映射逻辑：如果轨迹的历史包含当前检测框，则认为该检测属于该轨迹（与 SortTracker 行为一致）。
    /// </summary>
    public void Track(Frame frame)
    {
        frame.Retain();
        if (frame == null) throw new ArgumentNullException(nameof(frame));
        if (_matcher == null) throw new InvalidOperationException("Matcher not initialized.");
        //Console.WriteLine($"DetectedObjects count: {frame.DetectedObjects.Count}");
        var detectedObjects = frame.DetectedObjects ?? Array.Empty<Perceptron.Domain.Entity.ObjectDetection.DetectedObject>();

        // 过滤掉低置信度检测，避免无意义的 ReID 调用
        var candidates = detectedObjects
            .Select((d, idx) => (Obj: d, Index: idx))
            .Where(x => x.Obj.Confidence >= _targetConfidence)
            .ToArray();
        //if (detectedObjects.Count != candidates.Length)
        //{
        //   Console.WriteLine($"Warning: DetectedObjects count ({detectedObjects.Count}) does not match tracks count ({candidates.Length}).");
        //}

        if (candidates.Length == 0)
        {
            // 没有候选项时不修改 TrackingId
            return;
        }

        IPrediction[] predictions = candidates
            .Select(c => new FramePrediction(c.Obj))
            .ToArray();
        //Console.WriteLine($"Filtered object count: {predictions.Length}");
        IReadOnlyList<Tracker.DeepSort.Matchers.Abstract.ITrack> tracks = _matcher.Run(frame.Scene, predictions);
        //if (detectedObjects.Count != tracks.Count)
        //{
        //    Console.WriteLine($"Warning: DetectedObjects count ({detectedObjects.Count}) does not match tracks count ({tracks.Count}). This may indicate a mismatch in the matching process.");
        //}

        // 将轨迹 Id 回写回原始 DetectedObjects（如果轨迹历史包含当前检测框）
        // 同时收集被匹配到的 DetectedObject 引用，最终从 frame 中移除未匹配的项
        var matched = new HashSet<Perceptron.Domain.Entity.ObjectDetection.DetectedObject>();
        foreach (var detectedObject in detectedObjects)
        {
            var bbox = detectedObject.Bbox;
            var roi = new RectangleF(bbox.X, bbox.Y, bbox.Width, bbox.Height);

            foreach (var track in tracks)
            {
                if (track.History != null && track.History.Contains(roi))
                {
                    detectedObject.TrackingId = track.Id;
                    matched.Add(detectedObject);
                    break;
                }
            }
        }

        // 将未被匹配到的 detected object 从 frame 中移除
        // 注意：Frame.DetectedObjects 是可写的 IReadOnlyList，因此这里构建新的 List 并赋值回去
        var resulting = detectedObjects.Where(d => matched.Contains(d)).ToList();
        //if (resulting.Count != detectedObjects.Count)
        //{
        //    Console.WriteLine($"Warning: Only {resulting.Count} out of {detectedObjects.Count} detected objects were matched to tracks. Unmatched objects will be removed from the frame.");
        //}
        frame.DetectedObjects = resulting;
        frame.Dispose();
    }

    public void Dispose()
    {
        try
        {
            _matcher?.Dispose();
        }
        catch { }

        try
        {
            _appearanceExtractor?.Dispose();
        }
        catch { }

        GC.SuppressFinalize(this);
    }

    private static string? TryGet(Dictionary<string, string> prefs, string key) =>
        prefs.TryGetValue(key, out var v) ? v : null;

    private static int ParseInt(Dictionary<string, string> prefs, string key, int @default)
    {
        if (prefs.TryGetValue(key, out var v) && int.TryParse(v, out var i))
            return i;
        return @default;
    }

    private static float ParseFloat(Dictionary<string, string> prefs, string key, float @default)
    {
        if (prefs.TryGetValue(key, out var v) && float.TryParse(v, out var f))
            return f;
        return @default;
    }

    /// <summary>
    /// 将 Baize 的 DetectedObject 适配为 DeepSort 的 IPrediction。
    /// </summary>
    private sealed class FramePrediction : IPrediction
    {
        private readonly Perceptron.Domain.Entity.ObjectDetection.DetectedObject _detected;

        public FramePrediction(Perceptron.Domain.Entity.ObjectDetection.DetectedObject detected)
        {
            _detected = detected ?? throw new ArgumentNullException(nameof(detected));
            CurrentBoundingBox = new Rectangle(detected.Bbox.X, detected.Bbox.Y, detected.Bbox.Width, detected.Bbox.Height);
        }

        //public DetectionObjectType DetectionObjectType
        //    => Enum.IsDefined(typeof(DetectionObjectType), (byte)_detected.LabelId)
        //       ? (DetectionObjectType)(byte)_detected.LabelId
        //       : DetectionObjectType.Person;

        public int DetectionObjectType => _detected.LabelId;

        public Rectangle CurrentBoundingBox { get; }

        public float Confidence => _detected.Confidence;
    }

    /// <summary>
    /// 直接使用传入参数构建 IAppearanceExtractor。
    /// 目前实现：
    ///   - version == 0 -> ReidScorer&lt;CustomReidModel&gt;
    /// 若需支持 OSNet/FastReid 等，扩展此方法并确保对应模型类型在项目中可用。
    /// 新增：支持通过 sessionDevice 选择 CPU 或 CUDA（传入 "cpu" 或 "cuda"）。
    /// </summary>
    private static IAppearanceExtractor ConstructAppearanceExtractorFromOptions(int appearanceVersion, string filePath, string sessionDevice, int? extractorsInMemory = 4)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Appearance extractor model file not found.", filePath);

        const int DefaultExtractorsCount = 4;
        int extractorsCount = extractorsInMemory ?? DefaultExtractorsCount;

        SessionOptions sessionOptions = CreateSessionOptions(sessionDevice);

        return appearanceVersion switch
        {
            0 => new ReidScorer<OSNet_x1_0>(File.ReadAllBytes(filePath), extractorsCount, sessionOptions: sessionOptions),
            1 => new ReidScorer<Fast_Reid_mobilenetv2>(File.ReadAllBytes(filePath), extractorsCount, sessionOptions: sessionOptions),

            _ => throw new NotSupportedException($"AppearanceExtractorVersion '{appearanceVersion}' is not supported."),
        };
    }

    /// <summary>
    /// 根据 sessionDevice 字符串创建 SessionOptions。
    /// 支持 "cpu"（默认）和 "cuda"（会调用 SessionOptions.MakeSessionOptionWithCudaProvider()）。
    /// 未知的值会抛出异常以提醒配置错误。
    /// </summary>
    private static SessionOptions CreateSessionOptions(string sessionDevice)
    {
        if (string.IsNullOrWhiteSpace(sessionDevice))
            sessionDevice = "cpu";

        switch (sessionDevice.Trim().ToLowerInvariant())
        {
            case "cuda":
            case "gpu":
                // 如果项目中提供了 MakeSessionOptionWithCudaProvider 扩展（已在其它地方使用），直接调用它
                try
                {
                    return SessionOptions.MakeSessionOptionWithCudaProvider();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to create CUDA SessionOptions. Ensure CUDA provider is available and MakeSessionOptionWithCudaProvider is implemented.", ex);
                }

            case "cpu":
            case "coreML":
                return new SessionOptions();

            default:
                throw new NotSupportedException($"Unknown SessionDevice '{sessionDevice}'. Supported values: 'cpu', 'cuda'/'gpu', 'coreML'.");
        }
    }

    // 实现 ComponentBase 的抽象方法 LoadPreferences
    protected override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        // DeepSortTracker 构造函数已直接处理了 preferences，
        // 此处可留空或根据需要实现参数重载逻辑
    }
}