using System.Reflection;

namespace Algorithm.Common.Tests;

public class Stage6MigrationContractTests
{
    [Test]
    public void ObjectOccurrenceByLlm_UsesLlmAlgorithmTemplate()
    {
        var algorithmType = typeof(Algorithm.General.ObjectOccurrenceByLLM.Executor);
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(algorithmType.BaseType, Is.EqualTo(typeof(LlmAlgorithmBase)));
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Initialize)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Analyze)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == "ProcessEvent"),
                Is.False);
        });
    }

    [Test]
    public void ObjectOccurrenceByLlmEvent_UsesCommonEventDispatcherContract()
    {
        Assert.That(
            typeof(IAnnotatedAlgorithmEvent).IsAssignableFrom(
                typeof(Algorithm.General.ObjectOccurrenceByLLM.Event.ObjectOccurrenceLLMEvent)),
            Is.True);
    }

    [Test]
    public void SequenceToImage_UsesLlmAlgorithmTemplate()
    {
        var algorithmType = typeof(Algorithm.General.SequenceToImage.Executor);
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(algorithmType.BaseType, Is.EqualTo(typeof(LlmAlgorithmBase)));
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Initialize)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Analyze)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == "ProcessEvent"),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Dispose)),
                Is.False);
        });
    }

    [Test]
    public void SequenceImageLlmEvent_UsesCommonEventDispatcherContract()
    {
        Assert.That(
            typeof(IAnnotatedAlgorithmEvent).IsAssignableFrom(
                typeof(Algorithm.General.SequenceToImage.Event.SequenceImageLLMEvent)),
            Is.True);
    }

    [Test]
    public void ShipLabelsByLlm_UsesLlmAlgorithmTemplate()
    {
        var algorithmType = typeof(Algorithm.Ship.LabelsByLLM.Executor);
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(algorithmType.BaseType, Is.EqualTo(typeof(LlmAlgorithmBase)));
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Initialize)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Analyze)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == "ProcessEvent"),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == nameof(AlgorithmBase.Dispose)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == "SetSubscriber"),
                Is.False);
        });
    }

    [Test]
    public void ShipLabelsByLlmEvent_UsesCommonEventDispatcherContract()
    {
        Assert.That(
            typeof(IAnnotatedAlgorithmEvent).IsAssignableFrom(
                typeof(Algorithm.Ship.LabelsByLLM.Event.ShipLabelEvent)),
            Is.True);
    }
}
