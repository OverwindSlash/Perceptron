using MessagePipe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.AlgorithmModule;
using Perceptron.Domain.Abstraction.Annotation;
using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.Abstraction.MediaLoader;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.ObjectDetector;
using Perceptron.Domain.Abstraction.ObjectTracker;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Event.SnapshotManager;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline.Extension;
using Serilog;
using System.Collections.Concurrent;

namespace Perceptron.Service.Pipeline;

public class AnalysisPipeline : FrameAndObjectExpiredSubscriber
{
    // Settings
    private PipelineSettings _pipeLineSettings;
    private List<VideoLoaderSettings> _videoLoaderSettings;
    private FrameBufferSettings _inputFrameBufferSettings;
    private FrameBufferSettings _outputFrameBufferSettings;
    private DetectorSettings _detectorSettings;
    private List<RegionManagerSettings> _regionManagerSettings;
    private TrackerSettings _trackerSettings;
    private SnapshotSettings _snapshotSettings;
    private MessagePosterSettings _messagePosterSettings;
    private EventRepositorySettings _eventRepositorySettings;
    private AnnotationSenderSettings _annotationSenderSettings;
    private AnnotationRenderSettings _annotationRenderSettings;

    private List<AlgorithmSettings> _algorithmSettings;

    // dependency injection
    private ServiceCollection _services;
    public ServiceProvider Provider { get; private set; }

    // pipeline video frame slide window
    private VideoFrameSlideWindow _slideWindow;

    // components
    public IVideoFrameBuffer InputFrameBuffer { get; private set; }
    public IVideoFrameBuffer OutputFrameBuffer { get; private set; }
    public List<IVideoLoader> VideoLoaders { get; private set; }
    public IObjectDetector ObjectDetector { get; private set; }
    public List<IRegionManager> RegionManagers { get; private set; }
    public IObjectTracker ObjectTracker { get; private set; }
    public ISnapshotManager SnapshotManager { get; private set; }
    public IMessagePoster MessagePoster { get; private set; }
    public IEventRepository EventRepository { get; private set; }
    public IAnnotationSender AnnotationSender { get; private set; }
    public IAnnotationRender AnnotationRender { get; private set; }
    public List<IAlgorithmModule> AlgorithmModules { get; private set; }

    // object id to gui mapping
    private ConcurrentDictionary<string, string> _objectId2GuiDictionary = new ConcurrentDictionary<string, string>();

    public AnalysisPipeline(IConfiguration config)
    {
        Log.Information("Create analysis pipeline...");

        LoadSettings(config);

        RegisterComponents();

        InitializeComponents();

        Log.Information("Analysis pipeline created.");
    }

    private void LoadSettings(IConfiguration config)
    {
        // pipeline
        _pipeLineSettings = config.GetSection("Pipeline").Get<PipelineSettings>()
                            ?? throw new InvalidDataException("Pipeline settings corrupted.");

        // video loaders
        _videoLoaderSettings = config.GetSection("VideoLoaders").Get<List<VideoLoaderSettings>>()
                               ?? throw new InvalidDataException("VideoLoader settings corrupted.");
        foreach (var setting in _videoLoaderSettings)
        {
            setting.ParsePreferences();
        }

        // frame buffers
        _inputFrameBufferSettings = config.GetSection("InputFrameBuffer").Get<FrameBufferSettings>()
                                    ?? throw new InvalidDataException("InputFrameBuffer settings corrupted.");
        _inputFrameBufferSettings.ParsePreferences();

        _outputFrameBufferSettings = config.GetSection("OutputFrameBuffer").Get<FrameBufferSettings>()
                                     ?? throw new InvalidDataException("OutputFrameBuffer settings corrupted.");
        _outputFrameBufferSettings.ParsePreferences();

        // detector
        _detectorSettings = config.GetSection("Detector").Get<DetectorSettings>()
                            ?? throw new InvalidDataException("Detector settings corrupted.");
        _detectorSettings.ParsePreferences();

        // region managers
        _regionManagerSettings = config.GetSection("RegionManager").Get<List<RegionManagerSettings>>()
                                 ?? throw new InvalidDataException("RegionManager settings corrupted.");
        foreach (var setting in _regionManagerSettings)
        {
            setting.ParsePreferences();
        }

        // tracker
        _trackerSettings = config.GetSection("Tracker").Get<TrackerSettings>()
                           ?? throw new InvalidDataException("Tracker settings corrupted.");
        _trackerSettings.ParsePreferences();

        // snapshot manager
        _snapshotSettings = config.GetSection("Snapshot").Get<SnapshotSettings>()
                            ?? throw new InvalidDataException("Snapshot settings corrupted.");
        _snapshotSettings.ParsePreferences();

        // message poster
        _messagePosterSettings = config.GetSection("MessagePoster").Get<MessagePosterSettings>()
                                 ?? throw new InvalidDataException("MessagePoster settings corrupted.");
        _messagePosterSettings.ParsePreferences();

        // event repository
        _eventRepositorySettings = config.GetSection("EventRepository").Get<EventRepositorySettings>()
                                   ?? throw new InvalidDataException("EventRepository settings corrupted.");
        _eventRepositorySettings.ParsePreferences();

        // annotation
        _annotationSenderSettings = config.GetSection("AnnotationSender").Get<AnnotationSenderSettings>()
                                     ?? throw new InvalidDataException("AnnotationSender settings corrupted.");
        _annotationSenderSettings.ParsePreferences();

        _annotationRenderSettings = config.GetSection("AnnotationRender").Get<AnnotationRenderSettings>()
                                    ?? throw new InvalidDataException("AnnotationRender settings corrupted.");
        _annotationRenderSettings.ParsePreferences();

        // algorithm modules
        _algorithmSettings = config.GetSection("Algorithms").Get<List<AlgorithmSettings>>()
                             ?? throw new InvalidDataException("Algorithm settings corrupted.");
    }

    private void RegisterComponents()
    {
        Log.Information("Registering components...");

        _services = new ServiceCollection();

        _services.AddMessagePipe();

        _services.AddPipeline(this);

        _services.AddComponent<IVideoFrameBuffer>(_inputFrameBufferSettings);
        _services.AddComponent<IVideoFrameBuffer>(_outputFrameBufferSettings);

        foreach (var setting in _videoLoaderSettings)
        {
            _services.AddComponent<IVideoLoader>(setting);
        }

        _services.AddComponent<IObjectDetector>(_detectorSettings);

        foreach (var setting in _regionManagerSettings)
        {
            _services.AddComponent<IRegionManager>(setting);
        }

        _services.AddComponent<IObjectTracker>(_trackerSettings);
        _services.AddComponent<ISnapshotManager>(_snapshotSettings);

        _services.AddComponent<IMessagePoster>(_messagePosterSettings);
        _services.AddComponent<IEventRepository>(_eventRepositorySettings);

        _services.AddComponent<IAnnotationSender>(_annotationSenderSettings);
        _services.AddComponent<IAnnotationRender>(_annotationRenderSettings);

        foreach (var settings in _algorithmSettings)
        {
            _services.AddAlgorithm<IAlgorithmModule>(settings, this);
        }

        Provider = _services.BuildServiceProvider();

        _slideWindow = new VideoFrameSlideWindow(_pipeLineSettings.FrameLifetime);

        Log.Information("Components registered successfully.");
    }

    private void InitializeComponents()
    {
        Log.Information("Initialize components ...");

        // 获取事件订阅器
        var objectExpiredSubscriber = Provider.GetRequiredService<ISubscriber<ObjectExpiredEvent>>();
        var frameExpiredSubscriber = Provider.GetRequiredService<ISubscriber<FrameExpiredEvent>>();
        this.SetSubscriber(objectExpiredSubscriber);
        this.SetSubscriber(frameExpiredSubscriber);

        // 耗时组件优先于视频加载器初始化，以防止视频解码被延迟导致错误.
        ObjectDetector = Provider.GetRequiredService<IObjectDetector>();
        //ObjectDetector.Init(); // 延后初始化，为了兼容华为 Ascend 推理

        InputFrameBuffer = Provider.GetServices<IVideoFrameBuffer>()
                               .First(f => f.BufferName == "InputFrameBuffer");

        OutputFrameBuffer = Provider.GetServices<IVideoFrameBuffer>()
                                .First(f => f.BufferName == "OutputFrameBuffer");
        
        VideoLoaders = Provider.GetServices<IVideoLoader>().ToList();
        foreach (var videoLoader in VideoLoaders)
        {
            var sourceId = videoLoader.SourceId;
            var settings = _videoLoaderSettings.First(s => s.SourceId == sourceId);

            videoLoader.AttachBuffer(InputFrameBuffer);
            videoLoader.Open(settings.VideoUri);
        }

        // TODO: 考虑当多个 VideoLoader 的视频分辨率不同时的处理方案
        RegionManagers = Provider.GetServices<IRegionManager>().ToList();
        foreach (var regionManager in RegionManagers)
        {
            var videoLoader = VideoLoaders.First(v => v.SourceId == regionManager.SourceId);

            if (videoLoader == null)
            {
                throw new ArgumentException($"Can not found VideoLoader with SourceId:{regionManager.SourceId}");
            }

            regionManager.InitRegionDefinition(videoLoader.Specs.Width, videoLoader.Specs.Height);
            regionManager.SetSubscriber(objectExpiredSubscriber);
        }

        ObjectTracker = Provider.GetRequiredService<IObjectTracker>();

        SnapshotManager = Provider.GetRequiredService<ISnapshotManager>();
        SnapshotManager.SetPublisher(Provider.GetRequiredService<IPublisher<ObjectBestSnapshotCreatedEvent>>());
        SnapshotManager.SetSubscriber(objectExpiredSubscriber);
        SnapshotManager.SetSubscriber(frameExpiredSubscriber);

        MessagePoster = Provider.GetRequiredService<IMessagePoster>();

        EventRepository = Provider.GetRequiredService<IEventRepository>();

        AnnotationSender = Provider.GetRequiredService<IAnnotationSender>();
        AnnotationRender = Provider.GetRequiredService<IAnnotationRender>();
        
        AlgorithmModules = Provider.GetServices<IAlgorithmModule>().ToList();
        foreach (var algorithmModule in AlgorithmModules)
        {
            var initResult = algorithmModule.Initialize();
            if (!initResult)
            {
                Log.Warning("AlgorithmModules {AlgorithmModuleAlgorithmName} initialization failed.", algorithmModule.AlgorithmName);
            }
        }

        _slideWindow.SetPublisher(Provider.GetRequiredService<IPublisher<FrameExpiredEvent>>());
        _slideWindow.SetPublisher(Provider.GetRequiredService<IPublisher<ObjectExpiredEvent>>());

        Log.Information("Components Initialized successfully.");
    }

    public void Run()
    {
        Log.Information("Start analysis pipeline...");

        ObjectDetector.Init(RegionManagers); // 延后初始化，为了兼容华为 Ascend 推理

        List<Task> allTasks = [];
        List<Task> videoTasks = [];
        foreach (var videoLoader in VideoLoaders)
        {
            var videoTask = Task.Run(() =>
            {
                Log.Information("Open video source: {VideoUri}", videoLoader.VideoUri);
                videoLoader.Play();
            });
            
            allTasks.Add(videoTask);
            videoTasks.Add(videoTask);
        }

        var analysisTask = Task.Run(async () =>
        {
            Log.Information($"Begin analysis process...");

            while (!IsAllVideoCompleted(videoTasks) || InputFrameBuffer.Count != 0)
            {
                var frame = InputFrameBuffer.RetrieveFrame();
                if (frame == null) continue;

                // 1.detection
                if (!_detectorSettings.TileDetectionEnabled)
                {
                    frame.DetectedObjects = ObjectDetector.Detect(frame, _detectorSettings.ConfThresh, _detectorSettings.IouThresh);
                }
                else
                {
                    frame.DetectedObjects = ObjectDetector.DetectByTile(frame, _detectorSettings.TileDetectionSize,
                        _detectorSettings.ConfThresh, _detectorSettings.IouThresh);
                }

                // 2.region
                var regionManager = RegionManagers.First(r => r.SourceId == frame.SourceId);
                regionManager.CalcRegionProperties(frame);

                // 3.tracking
                ObjectTracker.Track(frame);

                // 4.snapshot
                SnapshotManager.ProcessSnapshots(frame);

                // 5.algorithm modules
                foreach (var algorithm in AlgorithmModules)
                {
                    var analysisResult = algorithm.Analyze(frame);
                }

                // 6. annotation sending
                await AnnotationSender.SendAsync(frame.Annotation);

                _slideWindow.AddNewFrame(frame);
                OutputFrameBuffer.PushFrame(frame);
            }
        });

        var displayTask = Task.Run(async () =>
        {
            while (!analysisTask.IsCompleted || OutputFrameBuffer.Count != 0)
            {
                var frame = OutputFrameBuffer.RetrieveFrame();
                if (frame == null) continue;

                RealtimeDisplay(frame);
            }
        });

        Task.WaitAll(allTasks);

        Log.Information($"Analysis complete.");
    }

    private static bool IsAllVideoCompleted(List<Task> videoTasks)
    {
        bool isAllVideosCompleted = true;

        foreach (var videoTask in videoTasks)
        {
            isAllVideosCompleted &= videoTask.IsCompleted;
        }

        return isAllVideosCompleted;
    }

    private void RealtimeDisplay(Frame frame)
    {
        if (_pipeLineSettings.EnableDebugDisplay)
        {
            using var image = frame.Scene.Clone();

            AnnotationRender.DrawAnnotations(image, frame.Annotation);

            // Fix: Resize returns a new Mat, so use the result as the argument
            var width = _pipeLineSettings.RealtimeDisplayWidth;
            using var resizedImage = image.Resize(new Size(width, image.Height * width / image.Width));
            Cv2.ImShow(_pipeLineSettings.RealtimeDisplayTitle, resizedImage);
            Cv2.WaitKey(1);
        }
    }

    public string QueryGuidByObjectId(string objectId)
    {
        if (_objectId2GuiDictionary.ContainsKey(objectId))
        {
            return _objectId2GuiDictionary[objectId];
        }
        else
        {
            string guid = Guid.NewGuid().ToString();
            _objectId2GuiDictionary.TryAdd(objectId, guid);
            return guid;
        }
    }

    public override void ProcessEvent(FrameExpiredEvent @event)
    {
        // TODO: 实现帧过期事件处理逻辑
    }

    public override void ProcessEvent(ObjectExpiredEvent @event)
    {
        _objectId2GuiDictionary.TryRemove(@event.Id, out _);
    }

    public override void Dispose()
    {
        _slideWindow.Dispose();
        Provider.Dispose();

        base.Dispose();
    }
}