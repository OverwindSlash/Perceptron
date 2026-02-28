using ComponentCommon;
using Detector.Common;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.ObjectDetector;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Extensions;
using Perceptron.Domain.Setting;
using Serilog;
using SkiaSharp;
using System.Diagnostics;
using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.ExecutionProvider.Cuda;
using YoloDotNet.Models;

namespace Detector.YoloDotNet;

public class YoloDetector : ComponentBase, IObjectDetector
{
    // model and execution settings
    private string _modelPath;
    private string _execProvider;
    private int _deviceId;
    private int _detectionStride;

    // target object types
    private List<string> _targetTypes;

    // detection filter
    private bool _filterSmallObject;
    private int _minBboxWidth;
    private int _minBboxHeight;

    private bool _filterLargeObject;
    private int _maxBboxWidth;
    private int _maxBboxHeight;

    // for region detection
    private bool _regionDetectionEnabled;
    private Rect _detectionRegion;

    // for tile detection and merging
    private bool _tileDetectionEnabled;
    private Tuple<int, int> _tileDetectionSize;
    private int _maxStitchGapPixel;
    private float _minVerticalOverlapRatio;
    private MergeOptions _mergeOptions;

    // for suppressing inner same object
    private bool _willSuppressInnerSameObject;
    private float _innerObjectOverlapRatio;

    // for object type mapping
    private bool _willMapObjectTypes;
    private List<string> _sourceTypeNames;
    private string _destinationTypeName;

    // region managers
    private List<IRegionManager> _regionManagers;

    // for yolo predictor
    private Yolo _predictor;
    private readonly List<string> _typeNames;
    private int _detectedCount;

    public YoloDetector(Dictionary<string, string>? preferences) 
        : base(preferences)
    {
        _modelPath = "yolo11m.onnx";
        _execProvider = "cpu";
        _deviceId = 0;
        _detectionStride = 1;

        _targetTypes = new List<string>();

        _filterSmallObject = false;
        _minBboxWidth = 0;
        _minBboxHeight = 0;

        _filterLargeObject = false;
        _maxBboxWidth = 1000;
        _maxBboxHeight = 1000;

        // _regionDetectionEnabled = false;
        // _detectionRegion = new Rect(0, 0, 0, 0);

        _tileDetectionEnabled = false;
        _tileDetectionSize = new Tuple<int, int>(1, 1);
        _maxStitchGapPixel = 2;                 // 切缝附近允许的最大像素间隙
        _minVerticalOverlapRatio = 0.9f;        // 垂直方向至少 90% 重合
        _mergeOptions = new MergeOptions
        {
            MaxStitchGapPx = _maxStitchGapPixel,
            MinOrthOverlapRatio = _minVerticalOverlapRatio,
            RequireSameClass = true,
            ConfidenceMode = ConfidenceAggregateMode.Max,
            TrackingMode = TrackingIdAggregateMode.MinNonZero
        };

        _willSuppressInnerSameObject = false;       // 是否过滤掉被同类型对象完全包含的DetectedObject
        _innerObjectOverlapRatio = 0.8f;            // 当IOU大于此阈值时，认为被包含

        _willMapObjectTypes = false;
        _sourceTypeNames = new List<string>();
        _destinationTypeName = string.Empty;
        
        _typeNames = new List<string>();
        _detectedCount = 0;
    }

    public void Init(List<IRegionManager> regionManagers)
    {
        Log.Information($"YoloDotNet detector initializing...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        _regionManagers = regionManagers;

        LoadPreferences(_preferences);

        CreatePredictor();

        Warmup();

        GenerateClassNames();

        stopwatch.Stop();
        Log.Information("YOlO v11 detector initialized in {StopwatchElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);
    }

    protected override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        _modelPath = DetectorSettings.ParseModelPath(preferences);
        _execProvider = DetectorSettings.ParseExecutionProvider(preferences);
        _deviceId = DetectorSettings.ParseDeviceId(preferences);
        _targetTypes = DetectorSettings.ParseTargetTypes(preferences);
        _detectionStride = DetectorSettings.ParseDetectionStride(preferences);
        _filterSmallObject = DetectorSettings.ParseFilterSmallObject(preferences);
        _minBboxWidth = DetectorSettings.ParseMinBboxWidth(preferences);
        _minBboxHeight = DetectorSettings.ParseMinBboxHeight(preferences);
        _filterLargeObject = DetectorSettings.ParseFilterLargeObject(preferences);
        _maxBboxWidth = DetectorSettings.ParseMaxBboxWidth(preferences);
        _maxBboxHeight = DetectorSettings.ParseMaxBboxHeight(preferences);
        //_regionDetectionEnabled = DetectorSettings.ParseRegionDetectionEnabled(preferences);
        //_detectionRegion = DetectorSettings.ParseDetectionRegion(preferences);
        _tileDetectionEnabled = DetectorSettings.ParseTileDetectionEnabled(preferences);
        _tileDetectionSize = DetectorSettings.ParseTileDetectionSize(preferences);
        _maxStitchGapPixel = DetectorSettings.ParseMaxStitchGapPixel(preferences);
        _minVerticalOverlapRatio = DetectorSettings.ParseMinVerticalOverlapRatio(preferences);
        _willSuppressInnerSameObject = DetectorSettings.ParseWillSuppressInnerSameObject(preferences);
        _innerObjectOverlapRatio = DetectorSettings.ParseInnerObjectOverlapRatio(preferences);
        _willMapObjectTypes = DetectorSettings.ParseWillMapObjectTypes(preferences);
        _sourceTypeNames = DetectorSettings.ParseSourceObjectTypeNames(preferences);
        _destinationTypeName = DetectorSettings.ParseDestinationObjectTypeName(preferences);

        Log.Verbose("Model: {ModelPath}, ExecProvider: {ExecProvider}, Device: {DeviceId}", _modelPath, _execProvider, _deviceId);
        Log.Verbose("TargetTypes: {Join}, DetectionStride: {DetectionStride}", string.Join(", ", _targetTypes), _detectionStride);
        Log.Verbose("FilterSmallObject:{FilterSmallObject}, MinBboxWidth: {MinBboxWidth}, MinBboxHeight: {MinBboxHeight}", _filterSmallObject, _minBboxWidth, _minBboxHeight);
        Log.Verbose("FilterLargeObject:{FilterLargeObject}, MaxBboxWidth: {MaxBboxWidth}, MaxBboxHeight: {MaxBboxHeight}", _filterLargeObject, _maxBboxWidth, _maxBboxHeight);
        //Log.Verbose("RegionDetectionEnabled: {RegionDetectionEnabled}, DetectionRegion: ({DetectionRegionX}, {DetectionRegionY}, {DetectionRegionWidth}, {DetectionRegionHeight})", _regionDetectionEnabled, _detectionRegion.X, _detectionRegion.Y, _detectionRegion.Width, _detectionRegion.Height);
        Log.Verbose("TileDetectionEnabled: {TileDetectionEnabled}, TileDetectionSize: {TileDetectionSize}, MaxStitchGapPixel: {MaxStitchGapPixel}, MinVerticalOverlapRatio: {MinVerticalOverlapRatio}", _tileDetectionEnabled, _tileDetectionSize, _maxStitchGapPixel, _minVerticalOverlapRatio);
        Log.Verbose("WillSuppressInnerSameObject: {WillSuppressInnerSameObject}, InnerOverlapRatio: {InnerObjectOverlapRatio}", _willSuppressInnerSameObject, _innerObjectOverlapRatio);
        Log.Verbose("WillMapObjectTypes: {WillMapObjectTypes}, Source: {Join}, To: {DestinationTypeName}", _willMapObjectTypes, string.Join(", ", _sourceTypeNames), _destinationTypeName);
    }

    private void CreatePredictor()
    {
        var yoloOptions = new YoloOptions()
        {
            //OnnxModel = _modelPath,
            ImageResize = ImageResize.Proportional,
            SamplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.None)
        };

        switch (_execProvider.ToLower())
        {
            case "cpu":
            case "cuda":
                yoloOptions.ExecutionProvider = new CudaExecutionProvider(_modelPath, _deviceId);
                break;
            // case "coreML":
            //     yoloOptions.ExecutionProvider = new CoreMLExecutionProvider(_modelPath);
            //     break;
            default:
                yoloOptions.ExecutionProvider = new CudaExecutionProvider(_modelPath, _deviceId);
                break;
        }

        _predictor?.Dispose();
        _predictor = new Yolo(yoloOptions);

        Log.Information($"Detector created.");
    }

    private void Warmup()
    {
        Log.Information($"Warm up detector...");

        using SKBitmap whiteBitmap = new SKBitmap(640, 640);

        Log.Information($"Warm up single image predictor...");
        _predictor.RunObjectDetection(whiteBitmap);

        Log.Information($"Warm up complete.");
    }

    private void GenerateClassNames()
    {
        _typeNames.Clear();
        var model = _predictor.OnnxModel;

        if (model.Labels != null)
        {
            foreach (var label in model.Labels)
            {
                _typeNames.Add(label.Name);
            }
        }

        Log.Information($"ModelTypesCount: {_typeNames.Count}");
    }

    public IReadOnlyList<DetectedObject> Detect(Frame frame, 
        float confThresh = YoloDefaults.DefaultConfidenceThreshold,
        float iouThresh = YoloDefaults.DefaultIouThreshold)
    {
        frame.Retain();

        if (_detectedCount++ % _detectionStride != 0)
        {
            frame.Dispose();
            return new List<DetectedObject>();
        }

        //Mat inputImage = GenerateRegionImage(frame.Scene);

        List<Tuple<Mat, Rect>> imageAndRegions = GenerateAnalysisImages(frame);

        List<DetectedObject> detectedObjects = new List<DetectedObject>();
        foreach (var imageAndRegion in imageAndRegions)
        {
            var image = imageAndRegion.Item1;
            var rect = imageAndRegion.Item2;

            using SKBitmap bitmap = image.ToSKBitmap();

            var detections = _predictor.RunObjectDetection(bitmap, confThresh, iouThresh);
            List<DetectedObject> analysisAreaDetections = GenerateDetectedObjects(frame, ConvertToYoloPrediction(detections, rect));
            analysisAreaDetections = FilterDetectedObjects(analysisAreaDetections);

            detectedObjects.AddRange(analysisAreaDetections);

            image.Dispose();
        }

        _detectedCount++;

        frame.Dispose();
        return detectedObjects;
    }

    private List<Tuple<Mat, Rect>> GenerateAnalysisImages(Frame frame)
    {
        var regionManager = _regionManagers.First(m => m.SourceId == frame.SourceId);
        var regionDefinition = regionManager.RegionDefinition;

        var imageAndRect = new List<Tuple<Mat, Rect>>(regionDefinition.AnalysisAreas.Count);

        foreach (var analysisArea in regionDefinition.AnalysisAreas)
        {
            var analysisAreaRect = analysisArea.GetBoundingBox();
            var analysisImage = new Mat(frame.Scene, analysisAreaRect);
            imageAndRect.Add(new Tuple<Mat, Rect>(analysisImage, analysisAreaRect));
        }

        return imageAndRect;
    }

    private Mat GenerateRegionImage(Mat image)
    {
        Mat inputImage = image;

        if (_regionDetectionEnabled)
        {
            if ((_detectionRegion.X < image.Width && _detectionRegion.X + _detectionRegion.Width <= image.Width) &&
                (_detectionRegion.Y < image.Height && _detectionRegion.Y + _detectionRegion.Height <= image.Height))
            {
                inputImage = new Mat(image, _detectionRegion);
            }
        }

        return inputImage;
    }

    private List<DetectedObject> GenerateDetectedObjects(Frame frame, IReadOnlyList<YoloPrediction> yoloPredictions)
    {
        int regionXOffset = _regionDetectionEnabled ? _detectionRegion.X : 0;
        int regionYOffset = _regionDetectionEnabled ? _detectionRegion.Y : 0;

        var detectedObjects = new List<DetectedObject>();
        foreach (var prediction in yoloPredictions)
        {
            var box = prediction.BBox;

            var boundingBox = BoundingBox.CreateFromRect(
                x: box.X + regionXOffset,
                y: box.Y + regionYOffset,
                width: box.Width,
                height: box.Height
            );

            var detectedObject = new DetectedObject(
                sourceId: frame.SourceId,
                frameId: frame.FrameId,
                utcTimeStamp: frame.UtcTimeStamp,
                labelId: prediction.TypeId,
                label: _typeNames[prediction.TypeId],
                confidence: prediction.Confidence,
                bbox: boundingBox
            );

            if (_targetTypes.Count == 0 || _targetTypes.Contains(detectedObject.Label.ToLower()))
            {
                detectedObjects.Add(detectedObject);
            }
        }

        return detectedObjects;
    }

    private List<YoloPrediction> ConvertToYoloPrediction(List<ObjectDetection> objectDetections, Rect rect = new Rect())
    {
        var yoloPredictions = new List<YoloPrediction>(objectDetections.Count);

        foreach (var objectDetection in objectDetections)
        {
            var yoloPrediction = new YoloPrediction
            {
                TypeId = objectDetection.Label.Index,
                Type = objectDetection.Label.Name,
                Confidence = (float)objectDetection.Confidence,
                BBox = new Rect()
                {
                    X = objectDetection.BoundingBox.Left + rect.X,
                    Y = objectDetection.BoundingBox.Top + rect.Y,
                    Width = objectDetection.BoundingBox.Width,
                    Height = objectDetection.BoundingBox.Height
                },
                TrackingId = 0
            };

            yoloPredictions.Add(yoloPrediction);
        }

        return yoloPredictions;
    }

    private List<DetectedObject> FilterDetectedObjects(List<DetectedObject> detectedObjects)
    {
        if (_targetTypes.Count != 0)
        {
            detectedObjects = DetectionFilter.FilterObjectTypes(detectedObjects, _targetTypes);
        }

        if (_filterLargeObject)
        {
            detectedObjects = DetectionFilter.FilterLargeObjects(detectedObjects, _maxBboxWidth, _maxBboxHeight);
        }

        if (_filterSmallObject)
        {
            detectedObjects = DetectionFilter.FilterSmallObjects(detectedObjects, _minBboxWidth, _minBboxHeight);
        }

        if (_willMapObjectTypes)
        {
            var mappedId = _typeNames.IndexOf(_destinationTypeName);
            detectedObjects = DetectionFilter.MapObjectType(detectedObjects, _sourceTypeNames, mappedId, _destinationTypeName);
        }

        if (_willSuppressInnerSameObject)
        {
            detectedObjects = DetectionFilter.FilterInnerSameObjects(detectedObjects, _innerObjectOverlapRatio);
        }

        return detectedObjects;
    }

    public IReadOnlyList<IReadOnlyList<DetectedObject>> DetectBatch(List<Frame> frames, 
        float confThresh = YoloDefaults.DefaultConfidenceThreshold,
        float iouThresh = YoloDefaults.DefaultIouThreshold)
    {
        var batchPredictions = new List<List<ObjectDetection>>();

        foreach (var frame in frames)
        {
            frame.Retain();

            // Load input image as SKBitmap (or SKImage)
            using var img = frame.Scene.ToSKBitmap();

            // Run object detection inference
            var results = _predictor.RunObjectDetection(img, confThresh, iouThresh);

            batchPredictions.Add(new List<ObjectDetection>(results));
        }

        List<IReadOnlyList<DetectedObject>> batchResult = new List<IReadOnlyList<DetectedObject>>(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            List<DetectedObject> detectedObjects = GenerateDetectedObjects(frames[i], ConvertToYoloPrediction(batchPredictions[i]));

            detectedObjects = FilterDetectedObjects(detectedObjects);

            batchResult.Add(detectedObjects);

            frames[i].Dispose();
        }

        return batchResult;
    }

    public IReadOnlyList<DetectedObject> DetectByTile(Frame frame, Tuple<int, int> tileSettings, 
        float confThresh = YoloDefaults.DefaultConfidenceThreshold,
        float iouThresh = YoloDefaults.DefaultIouThreshold)
    {
        frame.Retain();

        int rows = tileSettings.Item1;
        int cols = tileSettings.Item2;

        if (rows <= 0 || cols <= 0)
        {
            throw new ArgumentException("Grid settings must have positive values for rows and columns.");
        }

        // Slice the frame.Scene into a grid of sub-images
        int subImageWidth = frame.Scene.Width / cols;
        int subImageHeight = frame.Scene.Height / rows;

        var tileSpecs = new List<ImageTile>(rows * cols);

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                int x = j * subImageWidth;
                int y = i * subImageHeight;
                // Ensure we don't exceed the image boundaries
                int width = (j == cols - 1) ? frame.Scene.Width - x : subImageWidth;
                int height = (i == rows - 1) ? frame.Scene.Height - y : subImageHeight;
                Rect roi = new Rect(x, y, width, height);
                using Mat subImage = new Mat(frame.Scene, roi);

                // Load input image as SKBitmap (or SKImage)
                using var img = subImage.ToSKBitmap();

                // Run object detection inference
                var results = _predictor.RunObjectDetection(img, confThresh, iouThresh);

                var tileSpec = new ImageTile()
                {
                    RowIndex = i,
                    ColIndex = j,
                    TileWidth = subImageWidth,
                    TileHeight = subImageHeight,
                    Predictions = ConvertToYoloPrediction(results)
                };

                tileSpecs.Add(tileSpec);
            }
        }

        List<YoloPrediction> merged = YoloPredictionMerger.MergeFromTiles(tileSpecs, _mergeOptions);

        List<DetectedObject> detectedObjects = GenerateDetectedObjects(frame, merged);

        detectedObjects = FilterDetectedObjects(detectedObjects);

        frame.Dispose();

        return detectedObjects;
    }

    public void Close()
    {
        // Do nothing here, as we handle disposal in Dispose method
    }

    public void Dispose()
    {
        if (_predictor != null)
        {
            _predictor.Dispose();
        }
    }
}