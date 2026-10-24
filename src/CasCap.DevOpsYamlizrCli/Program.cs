using CasCap.Commands;
using System.Reflection;

namespace CasCap;

/// <summary>Entry point and root command for the yamlizr command line interface.</summary>
[Command(Name = "yamlizr", Description = "Azure DevOps Classic Designer-to-YAML pipeline conversion tool.")]
[HelpOption("--help")]
[VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
[Subcommand(typeof(GenerateCommand))]
internal sealed class Program
{
    private static Task<int> Main(string[] args) => AppHost.RunAsync(args);

    public int OnExecute(CommandLineApplication app, IConsole console)
    {
        console.WriteLine("You must specify a subcommand.");
        app.ShowHelp();
        return 1;
    }

    private static string GetVersion()
        => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
