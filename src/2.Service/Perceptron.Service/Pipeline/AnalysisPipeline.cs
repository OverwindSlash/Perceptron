using MessagePipe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Abstraction.MediaLoader;
using Perceptron.Domain.Abstraction.ObjectDetector;
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
    private DetectorSettings _detectorSettings;

    // dependency injection
    private ServiceCollection _services;
    public ServiceProvider Provider { get; private set; }

    // pipeline video frame slide window
    private VideoFrameSlideWindow _slideWindow;

    // components
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

        _detectorSettings = config.GetSection("Detector").Get<DetectorSettings>()
                            ?? throw new InvalidDataException("Detector settings corrupted.");
        _detectorSettings.ParsePreferences();
    }

    private void RegisterComponents()
    {
        Log.Information("Registering components...");

        _services = new ServiceCollection();

        _services.AddMessagePipe();

        _services.AddPipeline(this);

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

        VideoLoaders = Provider.GetServices<IVideoLoader>().ToList();
        foreach (var videoLoader in VideoLoaders)
        {
            videoLoader.Open(videoLoader.VideoUri);
        }

        // TODO: 初始化其他组件

        Log.Information("Components Initialized successfully.");
    }

    public void Run()
    {
        var videoTask = Task.Run(() =>
        {
            foreach (var videoLoader in VideoLoaders)
            {
                Log.Information("Open video source: {VideoUri}", videoLoader.VideoUri);
                videoLoader.Play();
            }
        });
    }

    public override void ProcessEvent(FrameExpiredEvent @event)
    {
        throw new NotImplementedException();
    }

    public override void ProcessEvent(ObjectExpiredEvent @event)
    {
        throw new NotImplementedException();
    }
}