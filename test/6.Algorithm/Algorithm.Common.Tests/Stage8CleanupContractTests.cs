using Algorithm.Common.Event;
using Perceptron.Domain.Abstraction.EventHandler;
using System.Reflection;

namespace Algorithm.Common.Tests;

public class Stage8CleanupContractTests
{
    [Test]
    public void AlgorithmBase_HasNoLegacyLlmCompatibilitySurface()
    {
        var baseType = typeof(AlgorithmBase);
        var legacyFields = new[]
        {
            "LLMAnalysisPropertyName",
            "LLMAnalysisType",
            "LLMAnalysisPromptPropertyName",
            "LLMAnalysisImageJpegPropertyName",
            "LLMRequestIdPropertyName",
            "LLMRequesterAlgorithmNamePropertyName",
            "LLMCandidateEventIdPropertyName",
            "LLMQueuePolicyPropertyName",
            "LLMExpireAtUtcPropertyName",
            "WillPerformLLMAnalysis",
            "LLMPromptFile",
            "_userPrompt"
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(IEventSubscriber<LLMInferenceResultEvent>)
                    .IsAssignableFrom(baseType),
                Is.False);
            Assert.That(
                baseType.GetMethod(
                    "SetSubscriber",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly),
                Is.Null);
            Assert.That(
                baseType.GetMethod(
                    "ProcessEvent",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly),
                Is.Null);
            foreach (var fieldName in legacyFields)
            {
                Assert.That(
                    baseType.GetField(
                        fieldName,
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.NonPublic),
                    Is.Null,
                    fieldName);
            }
        });
    }

    [TestCase(nameof(AlgorithmBase.Initialize))]
    [TestCase(nameof(AlgorithmBase.Analyze))]
    [TestCase(nameof(AlgorithmBase.Dispose))]
    public void AlgorithmBase_PublicLifecycleMethod_IsClosed(string methodName)
    {
        var method = typeof(AlgorithmBase).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.DeclaredOnly);

        Assert.That(method, Is.Not.Null);
        Assert.That(method!.IsVirtual && !method.IsFinal, Is.False);
    }
}
