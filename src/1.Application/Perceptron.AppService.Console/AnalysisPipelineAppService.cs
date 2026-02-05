using Microsoft.Extensions.Configuration;
using Perceptron.Service.Pipeline;

namespace Perceptron.AppService.Console;

public class AnalysisPipelineAppService
{
    public void RunWithConfigFile(string configFile)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(configFile, true, true)
            .Build();

        using var pipeline = new AnalysisPipeline(config);

        pipeline.Run();
    }
}