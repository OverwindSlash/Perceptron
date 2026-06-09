using System.Reflection;

namespace Algorithm.Common.Tests;

public class Stage5MigrationContractTests
{
    [TestCase(typeof(Algorithm.General.RegionAccess.Executor))]
    [TestCase(typeof(Algorithm.Ship.Labels.Executor))]
    public void MigratedAlgorithm_DoesNotOverridePublicLifecycleMethods(Type algorithmType)
    {
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Initialize)), Is.False);
        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Analyze)), Is.False);
        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Dispose)), Is.False);
    }

    [TestCase(typeof(Algorithm.General.RegionAccess.Event.EnterRegionEvent))]
    [TestCase(typeof(Algorithm.General.RegionAccess.Event.InRegionEvent))]
    [TestCase(typeof(Algorithm.General.RegionAccess.Event.LeaveRegionEvent))]
    [TestCase(typeof(Algorithm.Ship.Labels.Event.ShipLabelEvent))]
    public void MigratedDomainEvent_ImplementsAnnotatedEventContract(Type eventType)
    {
        Assert.That(typeof(IAnnotatedAlgorithmEvent).IsAssignableFrom(eventType), Is.True);
    }

    [TestCase(typeof(Algorithm.General.RegionAccess.Executor))]
    [TestCase(typeof(Algorithm.Ship.Labels.Executor))]
    public void MigratedAlgorithm_DoesNotExposeIndependentSubscriptionEntryPoint(Type algorithmType)
    {
        var setSubscriber = algorithmType.GetMethod(
            "SetSubscriber",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.That(setSubscriber, Is.Null);
    }

    [Test]
    public void RegionAccess_CommonEventProcessor_RequiresAnnotatedEvent()
    {
        var method = typeof(Algorithm.General.RegionAccess.Executor).GetMethod(
            "ProcessRegionEventCommon",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var eventType = method!.GetGenericArguments().Single();

        Assert.That(
            eventType.GetGenericParameterConstraints(),
            Does.Contain(typeof(IAnnotatedAlgorithmEvent)));
    }

    [Test]
    public void ShipLabel_DoesNotOwnRuntimeFrameOrSnapshot()
    {
        var runtimeProperties = typeof(Algorithm.Ship.Labels.ShipLabel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.PropertyType)
            .ToList();

        Assert.That(runtimeProperties, Does.Not.Contain(typeof(Perceptron.Domain.Entity.VideoStream.Frame)));
        Assert.That(runtimeProperties, Does.Not.Contain(typeof(OpenCvSharp.Mat)));
    }

    [Test]
    public void ShipLabelEvidence_OwnsCachedSnapshotAndReturnsIndependentClone()
    {
        var evidenceType = typeof(Algorithm.Ship.Labels.ShipLabel).Assembly.GetType(
            "Algorithm.Ship.Labels.ShipLabelEvidence");

        Assert.That(evidenceType, Is.Not.Null);

        using var cachedSnapshot = new OpenCvSharp.Mat(
            4,
            6,
            OpenCvSharp.MatType.CV_8UC3,
            OpenCvSharp.Scalar.All(7));
        var evidence = Activator.CreateInstance(
            evidenceType!,
            new Algorithm.Ship.Labels.ShipLabel(),
            "{}",
            42L,
            DateTime.UtcNow,
            cachedSnapshot);
        var clone = (OpenCvSharp.Mat)evidenceType!
            .GetMethod("CloneSnapshot", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(evidence, null)!;

        Assert.That(clone.Data, Is.Not.EqualTo(cachedSnapshot.Data));

        ((IDisposable)evidence!).Dispose();

        Assert.That(cachedSnapshot.IsDisposed, Is.True);
        Assert.That(clone.IsDisposed, Is.False);
        clone.Dispose();
    }
}
