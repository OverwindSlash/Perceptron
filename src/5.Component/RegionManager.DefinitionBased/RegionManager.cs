using ComponentCommon;
using MessagePipe;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Entity.ObjectDetection;
using Perceptron.Domain.Entity.RegionDefinition;
using Perceptron.Domain.Entity.RegionDefinition.Geometric;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Event.Pipeline;
using Perceptron.Domain.Setting;
using Serilog;
using System.Collections.Concurrent;

namespace RegionManager.DefinitionBased;

public class RegionManager : ComponentBase, IRegionManager
{
    public string SourceId { get; private set; }
    public string RegionDefinitionFile { get; private set; }
    public ImageRegionDefinition RegionDefinition { get; private set; }

    public bool Initialized { get; private set; }

    // Id(type:trackingId) -> trakingId, actually it is a Set.
    private readonly ConcurrentDictionary<string, int> _allTrackingIdsUnderAnalysis;

    private ISubscriber<ObjectExpiredEvent> _subscriber;
    private IDisposable _disposableSubscriber;

    public RegionManager(Dictionary<string, string> preferences = null)
        : base(preferences)
    {
        _allTrackingIdsUnderAnalysis = new ConcurrentDictionary<string, int>();

        LoadPreferences(preferences);
    }

    protected override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        SourceId = RegionManagerSettings.ParseSourceId(preferences);
        RegionDefinitionFile = RegionManagerSettings.ParseRegionDefinitionFile(preferences);
    }

    public void InitRegionDefinition(int imageWidth, int imageHeight)
    {
        _allTrackingIdsUnderAnalysis.Clear();

        try
        {
            RegionDefinition = ImageRegionDefinition.LoadFromJson(RegionDefinitionFile, imageWidth, imageHeight);
            Initialized = true;
        }
        catch (Exception e)
        {
            Log.Warning("RegionDefinitionFile corrupted. RegionManager disabled.");
            Initialized = false;
        }
    }

    public void CalcRegionProperties(Frame frame)
    {
        if (!Initialized)
        {
            return;
        }

        foreach (DetectedObject detectedObject in frame.DetectedObjects)
        {
            DetermineAnalyzableObject(detectedObject);
            CalculateLane(detectedObject);
        }
    }

    private void DetermineAnalyzableObject(DetectedObject detectedObject)
    {
        if (RegionDefinition.IsObjectAnalyzableRetain &&
            _allTrackingIdsUnderAnalysis.ContainsKey(detectedObject.Id))
        {
            detectedObject.IsUnderAnalysis = true;
            return;
        }

        NormalizedPoint point = new NormalizedPoint(RegionDefinition.ImageWidth, RegionDefinition.ImageHeight,
            (int)detectedObject.CenterX, (int)detectedObject.CenterY);

        foreach (AnalysisArea analysisArea in RegionDefinition.AnalysisAreas)
        {
            if (analysisArea.IsPointInPolygon(point))
            {
                detectedObject.IsUnderAnalysis = true;
                break;
            }
        }

        foreach (ExcludedArea excludedArea in RegionDefinition.ExcludedAreas)
        {
            if (excludedArea.IsPointInPolygon(point))
            {
                detectedObject.IsUnderAnalysis = false;
                break;
            }
        }

        if (detectedObject.IsUnderAnalysis)
        {
            _allTrackingIdsUnderAnalysis.TryAdd(detectedObject.Id, detectedObject.TrackingId);
        }
    }

    private void CalculateLane(DetectedObject detectedObject)
    {
        if (!detectedObject.IsUnderAnalysis)
        {
            return;
        }

        NormalizedPoint point = new NormalizedPoint(RegionDefinition.ImageWidth, RegionDefinition.ImageHeight,
            (int)detectedObject.CenterX, (int)detectedObject.CenterY);

        foreach (Lane lane in RegionDefinition.Lanes)
        {
            if (lane.IsPointInPolygon(point))
            {
                detectedObject.SetProperty("LaneIndex", lane.Index);
            }
        }
    }

    public void SetSubscriber(ISubscriber<ObjectExpiredEvent> subscriber)
    {
        _subscriber = subscriber;
        _disposableSubscriber = _subscriber.Subscribe(ProcessEvent);
    }

    public void ProcessEvent(ObjectExpiredEvent @event)
    {
        Task.Run(() =>
        {
            ReleaseAnalyzableObjectById(@event.Id);
        });
    }

    private void ReleaseAnalyzableObjectById(string id)
    {
        if (_allTrackingIdsUnderAnalysis.ContainsKey(id))
        {
            _allTrackingIdsUnderAnalysis.TryRemove(id, out var value);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposableSubscriber?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    
}