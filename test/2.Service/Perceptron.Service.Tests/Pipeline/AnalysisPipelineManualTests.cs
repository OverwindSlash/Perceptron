using Microsoft.Extensions.Configuration;
using Perceptron.Service.Pipeline;

namespace Perceptron.Service.Tests.Pipeline;

[Explicit]
public class AnalysisPipelineManualTests
{
    [Test]
    public void Test_AnalysisPipeline_Initialization()
    {
        // Arrange
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("Pipeline/test-settings.json", true, true)
            .Build();

        // Act
        var pipeline = new AnalysisPipeline(config);
        pipeline.Run();
    }
}