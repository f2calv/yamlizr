using CasCap.Commands;
using CasCap.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace CasCap;

/// <summary>Entry point and root command for the yamlizr command line interface.</summary>
[Command(Name = "yamlizr", Description = "Azure DevOps Classic Designer-to-YAML pipeline conversion tool.")]
[HelpOption("--help")]
[VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
[Subcommand(typeof(GenerateCommand))]
internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration((context, builder) =>
            {
                //The tool runs from the global tool store, so the shipped defaults are loaded from the
                //assembly directory while a per-project override is loaded from the working directory.
                builder.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false)
                    .AddUserSecrets<Program>(optional: true)
                    .AddEnvironmentVariables();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                logging.AddConsole();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(PhysicalConsole.Singleton);
                services.Configure<AzureDevOpsOptions>(context.Configuration.GetSection(AzureDevOpsOptions.ConfigurationSectionName));
            });

        try
        {
            return await host.RunCommandLineApplicationAsync<Program>(args);
        }
        catch (CommandParsingException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);

            if (ex is UnrecognizedCommandParsingException uex && uex.NearestMatches.Any())
            {
                await Console.Error.WriteLineAsync();
                await Console.Error.WriteLineAsync("Did you mean this?");
                await Console.Error.WriteLineAsync($"    {uex.NearestMatches.First()}");
            }

            return 1;
        }
    }

    private int OnExecute(CommandLineApplication app, IConsole console)
    {
        console.WriteLine("You must specify a subcommand.");
        app.ShowHelp();
        return 1;
    }

    private static string GetVersion()
        => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
