using CasCap.Services;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.WebApi;
using ShellProgressBar;
using System.Diagnostics;

namespace CasCap.Commands;

/// <summary>
/// This base type provides shared functionality.
/// Also, declaring <see cref="HelpOptionAttribute"/> on this type means all types that inherit from it
/// will automatically support '--help'
/// </summary>
[HelpOption("--help")]
public abstract class CommandBase
{
    protected /*readonly*/ ILogger _logger;
    protected /*readonly*/ ILoggerFactory _loggerFactory;
    protected /*readonly*/ IConsole _console;
    protected /*readonly*/ IApiService _apiSvc;

    protected CommandBase(ILogger<CommandBase> logger, ILoggerFactory loggerFactory, IConsole console)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _console = console;
    }

    protected ProjectHttpClient _projectClient;
    protected BuildHttpClient _buildClient;
    protected ReleaseHttpClient _releaseClient;
    protected TaskAgentHttpClient _taskAgentClient;

    protected VssBasicCredential _credentials;
    protected VssConnection _connection;

    protected TeamProject _project;
    protected List<BuildDefinitionReference> buildDefinitionReferences;
    protected List<BuildDefinition> buildDefinitions;
    protected List<ReleaseDefinition> releaseDefinitions;

    protected ProgressBar pbar;
    protected ProgressBarOptions pbarOptions { get; set; } = new ProgressBarOptions
    {
        ProgressCharacter = '─',
        ForegroundColor = ConsoleColor.Yellow,
        ForegroundColorDone = ConsoleColor.DarkGreen,
        BackgroundColor = ConsoleColor.DarkGray,
        BackgroundCharacter = '\u2593',
        ProgressBarOnBottom = true,
        ShowEstimatedDuration = true,
    };

    protected ChildProgressBar childPBar;
    protected ProgressBarOptions childPbarOptions { get; set; } = new ProgressBarOptions
    {
        ProgressCharacter = '─',
        ForegroundColor = ConsoleColor.Yellow,
        ForegroundColorDone = ConsoleColor.DarkGreen,
        BackgroundColor = ConsoleColor.DarkGray,
        BackgroundCharacter = '\u2593',
        DisplayTimeInRealTime = true,
        CollapseWhenFinished = true,
    };

    protected async Task<bool> GetProject(string project)
    {
        _console.Write($"Retrieving Azure DevOps Project '{project}' ... ");
        try
        {
            _project = await _projectClient.GetProject(project);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        if (_project is not null)
            _console.WriteLine($" retrieved :)");
        else
            _console.Write($" not found :(");
        return _project is not null;
    }

    protected bool Connect(string PAT, string organisationUri)
    {
        var uri = new Uri(organisationUri);
        _console.Write($"Connecting to DevOps REST API, {uri} ...");
        try
        {
            _credentials = new VssBasicCredential(string.Empty, PAT);
            _connection = new VssConnection(uri, _credentials);
            _projectClient = _connection.GetClient<ProjectHttpClient>();
            _buildClient = _connection.GetClient<BuildHttpClient>();
            _releaseClient = _connection.GetClient<ReleaseHttpClient>();
            _taskAgentClient = _connection.GetClient<TaskAgentHttpClient>();
            _apiSvc = new ApiService(_loggerFactory.CreateLogger<ApiService>(), PAT);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _console.WriteLine($"Unable to authenticate with DevOps REST API :(");
            return false;
        }
        Console.WriteLine($" connected :)");
        return true;
    }
}
