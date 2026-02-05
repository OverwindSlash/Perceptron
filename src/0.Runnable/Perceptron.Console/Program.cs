using CommandLine;
using Perceptron.AppService.Console;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("log.txt",
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true)
    .CreateLogger();

// Parse command line arguments using CommandLineParser
string configFile = "config/default-settings.json"; // default value
var parser = new Parser(settings => settings.IgnoreUnknownArguments = true);
parser.ParseArguments<Options>(args)
    .WithParsed(o =>
    {
        if (!string.IsNullOrWhiteSpace(o.ConfigFile))
        {
            configFile = o.ConfigFile;
        }
    })
    .WithNotParsed(errors =>
    {
        foreach (var err in errors)
        {
            Log.Warning("Command line parse warning: {Error}", err);
        }
    });

Log.Information($"Analysis begin...");
Log.Information("Using configuration file: {ConfigFile}", configFile);

// press enter to continue when debugging
//#if DEBUG
//Console.WriteLine("Press Enter to continue...");
//Console.ReadLine();
//#endif

var pipelineAppService = new AnalysisPipelineAppService();

try
{
    pipelineAppService.RunWithConfigFile(configFile);
}
catch (Exception e)
{
    // 如果有 Inner Exception 的消息，也需要同时显示，并体现 Exception 的层级
    Log.Fatal(e, "Fatal error: {Message}", e.Message);
    if (e.InnerException != null)
    {
        Log.Fatal(e.InnerException, "Inner exception: {Message}", e.InnerException.Message);
    }
}
finally
{
    Log.CloseAndFlush();
}

// Command line options definition
class Options
{
    [Option('c', "config", Required = false, HelpText = "Path to configuration file.")]
    public string? ConfigFile { get; set; }
}