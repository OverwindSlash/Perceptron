using Algorithm.Common.LLM;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Abstraction.MessagePoster;
using Perceptron.Domain.Abstraction.Repository;
using Perceptron.Domain.Event;
using System.Reflection;

namespace Algorithm.Common.Tests;

public class Stage7MigrationContractTests
{
    [Test]
    public void GeneralLlmProvider_UsesAlgorithmTemplateWithoutConsumingResults()
    {
        var algorithmType = typeof(Algorithm.General.LLM.Executor);
        var declaredPublicMethods = algorithmType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(algorithmType.BaseType, Is.EqualTo(typeof(AlgorithmBase)));
            Assert.That(typeof(ILLMResultHandler).IsAssignableFrom(algorithmType), Is.False);
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
                    method.Name == nameof(AlgorithmBase.Dispose)),
                Is.False);
            Assert.That(
                declaredPublicMethods.Any(method =>
                    method.Name == "ProcessEvent"),
                Is.False);
        });
    }

    [Test]
    public void Dispose_WaitsForInferenceWorkerToExit()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        using var provider = services.BuildServiceProvider();
        var algorithm = new Algorithm.General.LLM.Executor(
            new AlgorithmRuntimeDependencies(
                provider,
                Array.Empty<Perceptron.Domain.Abstraction.RegionManager.IRegionManager>(),
                new AlgorithmEventDispatcherTests.FakeSnapshotManager(),
                new FakeEventRepository(),
                new FakeMessagePoster()),
            new Dictionary<string, string>
            {
                ["ApiKey"] = "test-key",
                ["ServerUrl"] = "http://127.0.0.1:1/v1"
            });

        algorithm.Initialize();
        var worker = (Thread?)typeof(Algorithm.General.LLM.Executor)
            .GetField(
                "_inferenceWorkerThread",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(algorithm);

        Assert.That(worker, Is.Not.Null);
        Assert.That(worker!.IsAlive, Is.True);

        var disposeTask = Task.Run(algorithm.Dispose);

        Assert.That(
            disposeTask.Wait(TimeSpan.FromSeconds(2)),
            Is.True,
            "LLM inference worker did not stop within the disposal timeout.");
        Assert.That(worker.IsAlive, Is.False);
    }

    [Test]
    public void Initialize_AfterCoreFailure_CanRetryAndDisposeWorker()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        using var provider = services.BuildServiceProvider();
        var preferences = new Dictionary<string, string>
        {
            ["ApiKey"] = "test-key",
            ["ServerUrl"] = "not-a-valid-url"
        };
        var algorithm = new Algorithm.General.LLM.Executor(
            new AlgorithmRuntimeDependencies(
                provider,
                Array.Empty<Perceptron.Domain.Abstraction.RegionManager.IRegionManager>(),
                new AlgorithmEventDispatcherTests.FakeSnapshotManager(),
                new FakeEventRepository(),
                new FakeMessagePoster()),
            preferences);

        Assert.Throws<UriFormatException>(() => algorithm.Initialize());

        preferences["ServerUrl"] = "http://127.0.0.1:1/v1";
        Assert.DoesNotThrow(() => algorithm.Initialize());

        var worker = (Thread?)typeof(Algorithm.General.LLM.Executor)
            .GetField(
                "_inferenceWorkerThread",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(algorithm);

        Assert.That(worker, Is.Not.Null);
        Assert.That(worker!.IsAlive, Is.True);

        var disposeTask = Task.Run(algorithm.Dispose);

        Assert.That(
            disposeTask.Wait(TimeSpan.FromSeconds(2)),
            Is.True,
            "LLM inference worker did not stop within the disposal timeout.");
        Assert.That(worker.IsAlive, Is.False);
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Task SaveDomainEventAsync(DomainEvent domainEvent) =>
            Task.CompletedTask;

        public Task<DomainEvent> LoadDomainEventAsync(string eventId) =>
            throw new NotSupportedException();

        public Task DeleteDomainEventAsync(string eventId) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMessagePoster : IMessagePoster
    {
        public void PostDomainEventMessage(DomainEvent @event)
        {
        }
    }
}
