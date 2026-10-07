using CasCap.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CasCap;

/// <summary>Builds and runs the yamlizr command-line host.</summary>
internal static class AppHost
{
    /// <summary>Configures the generic host and dispatches the requested command.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    internal static async Task<int> RunAsync(string[] args)
    {
        // The tool runs from the global tool store, so shipped defaults load from the assembly
        // directory while a per-project override loads from the working directory.
        var host = new HostBuilder()
            .ConfigureAppConfiguration((_, builder) => builder
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile(
                    Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
                    optional: true,
                    reloadOnChange: false)
                .AddUserSecrets<Program>(optional: true)
                .AddEnvironmentVariables())
            .ConfigureLogging((context, logging) =>
            {
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddConsole();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(PhysicalConsole.Singleton);
                services.Configure<AzureDevOpsOptions>(
                    context.Configuration.GetSection(AzureDevOpsOptions.ConfigurationSectionName));
            });

        try
        {
            return await host.RunCommandLineApplicationAsync<Program>(args);
        }
        catch (CommandParsingException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message);

            if (exception is UnrecognizedCommandParsingException unrecognizedException
                && unrecognizedException.NearestMatches.Any())
            {
                await Console.Error.WriteLineAsync();
                await Console.Error.WriteLineAsync("Did you mean this?");
                await Console.Error.WriteLineAsync($"    {unrecognizedException.NearestMatches.First()}");
            }

            return 1;
        }
    }
}