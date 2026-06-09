namespace Algorithm.General.SequenceToImage;

public sealed record SequenceImageFrameInfo(
    long FrameId,
    long OffsetMilliSec,
    DateTime UtcTimeStamp);
