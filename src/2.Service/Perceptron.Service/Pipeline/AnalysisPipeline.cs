using MessagePipe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.Abstraction.MediaLoader;
using Perceptron.Domain.Abstraction.ObjectDetector;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;
using Perceptron.Service.Pipeline.Extension;
using Serilog;

namespace Perceptron.Service.Pipeline;

public class AnalysisPipeline : FrameAndObjectExpiredSubscriber
{
    // Settings
    private PipelineSettings _pipeLineSettings;
    private List<VideoLoaderSettings> _videoLoaderSettings;
    private FrameBufferSettings _inputFrameBufferSettings;
    private FrameBufferSettings _outputFrameBufferSettings;
    private DetectorSettings _detectorSettings;
    
    // dependency injection
    private ServiceCollection _services;
    public ServiceProvider Provider { get; private set; }

    // pipeline video frame slide window
    private VideoFrameSlideWindow _slideWindow;

    // components
    public IVideoFrameBuffer InputFrameBuffer { get; private set; }
    public IVideoFrameBuffer OutputFrameBuffer { get; private set; }
    public List<IVideoLoader> VideoLoaders { get; private set; }
    public IObjectDetector? ObjectDetector { get; private set; }
    

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
        _pipeLineSettings = config.GetSection("Pipeline").Get<PipelineSettings>()
                            ?? throw new InvalidDataException("Pipeline settings corrupted.");

        _videoLoaderSettings = config.GetSection("VideoLoaders").Get<List<VideoLoaderSettings>>()
                               ?? throw new InvalidDataException("VideoLoader settings corrupted.");
        foreach (var setting in _videoLoaderSettings)
        {
            setting.ParsePreferences();
        }

        _inputFrameBufferSettings = config.GetSection("InputFrameBuffer").Get<FrameBufferSettings>()
                                    ?? throw new InvalidDataException("InputFrameBuffer settings corrupted.");
        _inputFrameBufferSettings.ParsePreferences();

        _outputFrameBufferSettings = config.GetSection("OutputFrameBuffer").Get<FrameBufferSettings>()
                                     ?? throw new InvalidDataException("OutputFrameBuffer settings corrupted.");
        _outputFrameBufferSettings.ParsePreferences();

        _detectorSettings = config.GetSection("Detector").Get<DetectorSettings>()
                            ?? throw new InvalidDataException("Detector settings corrupted.");
        _detectorSettings.ParsePreferences();

        // TODO: Load other component settings
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
        
        // TODO: 添加其他组件

        _slideWindow = new VideoFrameSlideWindow(_pipeLineSettings.FrameLifetime);

        Provider = _services.BuildServiceProvider();

        Log.Information("Components registered successfully.");
    }

    private void InitializeComponents()
    {
        Log.Information("Initialize components ...");

        // 获取事件订阅器
        var objectExpiredSubscriber = Provider.GetService<ISubscriber<ObjectExpiredEvent>>();
        var frameExpiredSubscriber = Provider.GetService<ISubscriber<FrameExpiredEvent>>();

        // 耗时组件优先于视频加载器初始化，以防止视频解码被延迟导致错误.
        ObjectDetector = Provider.GetService<IObjectDetector>();
        // ObjectDetector.Init(); // 延后初始化，为了兼容华为 Ascend 推理

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

        // TODO: 初始化其他组件

        this.SetSubscriber(objectExpiredSubscriber);
        this.SetSubscriber(frameExpiredSubscriber);

        _slideWindow.SetPublisher(Provider.GetRequiredService<IPublisher<FrameExpiredEvent>>());
        _slideWindow.SetPublisher(Provider.GetRequiredService<IPublisher<ObjectExpiredEvent>>());

        Log.Information("Components Initialized successfully.");
    }

    public void Run()
    {
        Log.Information("Start analysis pipeline...");

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

        var analysisTask = Task.Run(() =>
        {
            ObjectDetector.Init(); // 延后初始化，为了兼容华为 Ascend 推理

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

            // Fix: Resize returns a new Mat, so use the result as the argument
            var width = _pipeLineSettings.RealtimeDisplayWidth;
            using var resizedImage = image.Resize(new Size(width, image.Height * width / image.Width));
            Cv2.ImShow(_pipeLineSettings.RealtimeDisplayTitle, resizedImage);
            Cv2.WaitKey(1);
        }
    }

    public override void ProcessEvent(FrameExpiredEvent @event)
    {
        // TODO: 实现帧过期事件处理逻辑
    }

    public override void ProcessEvent(ObjectExpiredEvent @event)
    {
        // TODO: 实现对象过期事件处理逻辑
    }
}