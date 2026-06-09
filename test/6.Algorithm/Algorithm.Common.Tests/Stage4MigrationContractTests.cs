using System.Reflection;

namespace Algorithm.Common.Tests;

public class Stage4MigrationContractTests
{
    [TestCase(typeof(Algorithm.General.ObjectOccurrence.Executor))]
    [TestCase(typeof(Algorithm.General.Classify.Executor))]
    [TestCase(typeof(Algorithm.CoastGuard.SmugglingDetection.Executor))]
    public void MigratedAlgorithm_DoesNotOverridePublicLifecycleMethods(Type algorithmType)
    {
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Initialize)), Is.False);
        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Analyze)), Is.False);
        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Dispose)), Is.False);
    }

    [TestCase(typeof(Algorithm.General.ObjectOccurrence.Event.ObjectOccurrenceEvent))]
    [TestCase(typeof(Algorithm.General.Classify.Event.ObjectClassifiedEvent))]
    [TestCase(typeof(Algorithm.CoastGuard.SmugglingDetection.Event.SmugglingEvent))]
    public void MigratedDomainEvent_ImplementsAnnotatedEventContract(Type eventType)
    {
        Assert.That(typeof(IAnnotatedAlgorithmEvent).IsAssignableFrom(eventType), Is.True);
    }

    [Test]
    public void SmugglingDetection_DoesNotExposeIndependentSubscriptionEntryPoint()
    {
        var setSubscriber = typeof(Algorithm.CoastGuard.SmugglingDetection.Executor).GetMethod(
            "SetSubscriber",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.That(setSubscriber, Is.Null);
    }
}
