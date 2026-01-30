namespace Perceptron.Domain.Setting;

public class PipelineSettings
{
    public const int DefaultFrameLifetime = 300;
    public const bool DefaultEnableDebugDisplay = false;
    public const bool DefaultEnableDetectionRegionDisplay = false;
    public const bool DefaultEnableAnnotationServer = false;
    public const string DefaultAnnotationServerPrefix = "http://+:8080/annotations/";
    public const bool DefaultEnableAnnotationUdp = false;
    public const string DefaultAnnotationUdpHost = "127.0.0.1";
    public const int DefaultAnnotationUdpPort = 9999;
    public const int DefaultRealtimeDisplayWidth = 1920;
    public const string DefaultRealtimeDisplayTitle = "debug";

    public int FrameLifetime { get; set; } = DefaultFrameLifetime;
    public bool EnableDebugDisplay { get; set; } = DefaultEnableDebugDisplay;
    public bool EnableDetectionRegionDisplay { get; set; } = DefaultEnableDetectionRegionDisplay;
    public bool EnableAnnotationServer { get; set; } = DefaultEnableAnnotationServer;
    public string AnnotationServerPrefix { get; set; } = DefaultAnnotationServerPrefix;
    public bool EnableAnnotationUdp { get; set; } = DefaultEnableAnnotationUdp;
    public string? AnnotationUdpHost { get; set; } = DefaultAnnotationUdpHost;
    public int AnnotationUdpPort { get; set; } = DefaultAnnotationUdpPort;
    public int RealtimeDisplayWidth { get; set; } = DefaultRealtimeDisplayWidth;
    public string RealtimeDisplayTitle { get; set; } = DefaultRealtimeDisplayTitle;
}
