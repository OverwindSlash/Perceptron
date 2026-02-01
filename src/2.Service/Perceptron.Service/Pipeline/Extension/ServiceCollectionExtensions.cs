using Microsoft.Extensions.DependencyInjection;
using Perceptron.Domain.Setting;
using System.Reflection;

namespace Perceptron.Service.Pipeline.Extension;

public static class ServiceCollectionExtensions
{
    public static void AddPipeline(
        this ServiceCollection serviceCollection,
        AnalysisPipeline pipeline)
    {
        serviceCollection.AddSingleton<AnalysisPipeline>(sp => pipeline);
    }

    public static void AddComponent<T>(
        this ServiceCollection serviceCollection,
        ComponentSettings componentSettings)
        where T : class
    {
        var component = CreateInstance<T>(
            componentSettings.AssemblyFile, componentSettings.FullQualifiedClassName,
            new object?[] { componentSettings.Preferences });
        serviceCollection.AddSingleton<T>(sp => component);
    }

    private static T CreateInstance<T>(string assemblyFile, string fullQualifiedClassName, object?[] preferences)
    {
        Assembly assembly = Assembly.LoadFrom(assemblyFile);
        Type type = assembly.GetType(fullQualifiedClassName);

        T instance = default;
        if (preferences == null)
        {
            instance = (T)Activator.CreateInstance(type);
        }
        else
        {
            instance = (T)Activator.CreateInstance(type, preferences);
        }

        return instance;
    }

    public static void AddAlgorithm<T>(
        this ServiceCollection serviceCollection,
        AlgorithmSettings algorithmSettings, AnalysisPipeline pipeline)
        where T : class
    {
        var algorithm = CreateInstance<T>(
            algorithmSettings.AssemblyFile, algorithmSettings.FullQualifiedClassName,
            new object?[] { pipeline, algorithmSettings.Preferences });

        serviceCollection.AddSingleton<T>(sp => algorithm);
    }
}