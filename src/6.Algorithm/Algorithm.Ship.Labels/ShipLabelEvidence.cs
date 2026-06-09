using OpenCvSharp;

namespace Algorithm.Ship.Labels;

internal sealed class ShipLabelEvidence : IDisposable
{
    private Mat? _snapshot;

    public ShipLabel Labels { get; }
    public string JsonLabel { get; }
    public float Confidence => Labels.Confidence;
    public long FrameId { get; }
    public DateTime FrameUtcTimeStamp { get; }
    public int SnapshotWidth => GetSnapshot().Width;
    public int SnapshotHeight => GetSnapshot().Height;
    public int SnapshotArea => SnapshotWidth * SnapshotHeight;

    public ShipLabelEvidence(
        ShipLabel labels,
        string jsonLabel,
        long frameId,
        DateTime frameUtcTimeStamp,
        Mat snapshot)
    {
        Labels = labels ?? throw new ArgumentNullException(nameof(labels));
        JsonLabel = jsonLabel ?? throw new ArgumentNullException(nameof(jsonLabel));
        FrameId = frameId;
        FrameUtcTimeStamp = frameUtcTimeStamp;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public Mat CloneSnapshot()
    {
        return GetSnapshot().Clone();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }

    private Mat GetSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot == null || snapshot.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(ShipLabelEvidence));
        }

        return snapshot;
    }
}
