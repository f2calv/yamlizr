using CasCap.Commands;
using CasCap.Models;
using CasCap.Services;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
namespace CasCap
{
    [Command("yamlizr")]
    [Subcommand(typeof(GenerateCommand))]
    class Program : CommandBase
    {
        public Program(ILogger<Program> logger, IConsole console) : base(logger, console) { }

        static CommandLineApplication app;

        //[Required]
        //[Option("-pat", Description = "Azure DevOps PAT (Personal Access Token).", Inherited = true)]
        //public string PAT { get; }

        public async static Task<int> Main(string[] args)
        {
            var initialData = new Dictionary<string, string>
            {
                { $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}:{nameof(AzureDevOpsOptions.PAT)}", "????" }
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(initialData)
                .Build();

            var section = configuration.GetSection(AzureDevOpsOptions.sectionKey);
            var azureDevOpsOptions = section.Get<AzureDevOpsOptions>();

            var services = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(PhysicalConsole.Singleton)
                .Configure<AzureDevOpsOptions>(section)
                .AddSingleton(s => azureDevOpsOptions)
                .AddSingleton<IApiService>()
                .AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.AddDebug();
                    //ApplicationLogging.LoggerFactory = logging.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
                })
                .BuildServiceProvider();

            var result = 0;
            try
            {
                app = new CommandLineApplication<Program>();
                app.Conventions
                    .UseDefaultConventions()
                    .UseConstructorInjection(services);
                result = await app.ExecuteAsync(args);
            }
            catch (CommandParsingException ex)
            {
                await Console.Error.WriteLineAsync(ex.Message);

                if (ex is UnrecognizedCommandParsingException uex && uex.NearestMatches.Any())
                {
                    await Console.Error.WriteLineAsync();
                    await Console.Error.WriteLineAsync("Did you mean this?");
                    await Console.Error.WriteLineAsync("    " + uex.NearestMatches.First());
                }

                result = -1;
            }
            return result;
        }

        public async Task OnExecuteAsync()
        {
            await Task.Delay(0);
            _console.WriteLine("Specify a subcommand...");
            app.ShowHelp();
        }
    }
}