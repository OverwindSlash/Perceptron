using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.RegionManager;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Abstraction.SnapshotManager;

namespace Algorithm.Common;

public sealed class AlgorithmRuntimeDependencies
{
    public IServiceProvider Services { get; }
    public IReadOnlyList<IRegionManager> RegionManagers { get; }
    public ISnapshotManager SnapshotManager { get; }
    public IEventRepository EventRepository { get; }
    public IMessagePoster MessagePoster { get; }

    public AlgorithmRuntimeDependencies(
        IServiceProvider services,
        IReadOnlyList<IRegionManager> regionManagers,
        ISnapshotManager snapshotManager,
        IEventRepository eventRepository,
        IMessagePoster messagePoster)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        RegionManagers = regionManagers ?? throw new ArgumentNullException(nameof(regionManagers));
        SnapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        EventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        MessagePoster = messagePoster ?? throw new ArgumentNullException(nameof(messagePoster));
    }
}
