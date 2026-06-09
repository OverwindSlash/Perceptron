using System.Reflection;

namespace Algorithm.Common.Tests;

public class FirstBatchMigrationContractTests
{
    [TestCase(typeof(Algorithm.GenerateDebugAnnotations.Executor))]
    [TestCase(typeof(Algorithm.General.MotionDetection.Executor))]
    [TestCase(typeof(Algorithm.General.ObjectDensity.Executor))]
    public void MigratedAlgorithm_DoesNotOverridePublicLifecycleMethods(Type algorithmType)
    {
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Initialize)), Is.False);
        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Analyze)), Is.False);
        Assert.That(declaredPublicMethods.Any(method => method.Name == nameof(AlgorithmBase.Dispose)), Is.False);
    }

    [TestCase(typeof(Algorithm.General.MotionDetection.Event.MotionDetectedEvent))]
    [TestCase(typeof(Algorithm.General.ObjectDensity.Event.DensityExceedThresholdEvent))]
    public void MigratedDomainEvent_ImplementsAnnotatedEventContract(Type eventType)
    {
        Assert.That(typeof(IAnnotatedAlgorithmEvent).IsAssignableFrom(eventType), Is.True);
    }
}
