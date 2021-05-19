using CasCap.Commands;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
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

        public async static Task<int> Main(string[] args)
        {
            var services = new ServiceCollection()
                .AddSingleton(PhysicalConsole.Singleton)
                .AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.AddDebug();
                    ApplicationLogging.LoggerFactory = logging.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
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