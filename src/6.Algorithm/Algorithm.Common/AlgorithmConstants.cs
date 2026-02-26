namespace Algorithm.Common;

public class AlgorithmConstants
{
    // bbox
    public const bool DefaultWillGenerateBBox = true;
    public const string DefaultBBoxStrokeColor = "#8fce00";
    public const int DefaultBBoxStrokeWidth = 1;

    // object text
    public const bool DefaultWillGenerateObjText = true;
    public const string DefaultObjTextColor = "#ffea00";
    public const int DefaultObjTextFontSize = 20;
    public const bool DefaultShowLabel = true;
    public const bool DefaultShowTrackingId = true;
    public const bool DefaultShowConfidence = false;

    // region definition
    public const bool DefaultWillGenerateAnalysisAreas = true;
    public const string DefaultAnalysisAreaStrokeColor = "#7dda58";
    public const bool DefaultWillGenerateExcludeAreas = true;
    public const string DefaultExcludeAreaStrokeColor = "#e36667";
    public const bool DefaultWillGenerateLanes = true;
    public const string DefaultLanesStrokeColor = "#e8e8e8";
    public const bool DefaultWillGenerateInterestAreas = true;
    public const string DefaultInterestAreasStrokeColor = "#ffeca1";
    public const bool DefaultWillGenerateCountLines = true;
    public const string DefaultEnterLineStrokeColor = "#4e4e4e";
    public const int DefaultEnterLineWidth = 1;
    public const string DefaultLeaveLineStrokeColor = "#4e4e4e";
    public const int DefaultLeaveLineWidth = 1;

    // event
    public const bool DefaultWillPublishEventMessage = true;
    public const bool DefaultWillSaveEventSnapshot = true;
    public const bool DefaultWillSaveEventVideoClip = false;
    public const int DefaultLocalEventIntervalSec = 1;
    public const string DefaultEventSnapshotDir = "Events";
    public const string DefaultEventName = "UnknownEvent";
}