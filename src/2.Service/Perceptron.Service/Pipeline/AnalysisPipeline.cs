using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;
using Serilog;

namespace Perceptron.Service.Pipeline;

public class AnalysisPipeline : FrameAndObjectExpiredSubscriber
{
    // Settings
    private PipelineSettings _pipeLineSettings;

    // dependency injection
    private ServiceCollection _services;
    public ServiceProvider Provider { get; private set; }

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
    }

    private void RegisterComponents()
    {
        Log.Information("Registering components...");

        _services = new ServiceCollection();

        _services.AddMessagePipe();

        Provider = _services.BuildServiceProvider();

        Log.Information("Components registered successfully.");
    }

    private void InitializeComponents()
    {
        Log.Information("Initialize components ...");


        Log.Information("Components Initialized successfully.");
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