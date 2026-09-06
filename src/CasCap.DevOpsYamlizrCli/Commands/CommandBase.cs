using CasCap.Services;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.WebApi;
using ShellProgressBar;
using System.Collections.Concurrent;

namespace CasCap.Commands;

/// <summary>Shared state and Azure DevOps clients for every yamlizr command.</summary>
/// <remarks>
/// Declaring <see cref="HelpOptionAttribute"/> here gives every inheriting command <c>--help</c>
/// without repeating the attribute.
/// </remarks>
[HelpOption("--help")]
public abstract class CommandBase
{
    /// <summary>Logger for diagnostics.</summary>
    protected /*readonly*/ ILogger _logger;

    /// <summary>Factory used to create loggers for types constructed after the command starts.</summary>
    protected /*readonly*/ ILoggerFactory _loggerFactory;

    /// <summary>Console this command writes its user interface to.</summary>
    protected /*readonly*/ IConsole _console;

    /// <summary>Azure DevOps REST calls the official client libraries do not cover.</summary>
    protected /*readonly*/ IApiService _apiSvc;

    /// <summary>Initialises the shared dependencies.</summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="loggerFactory">Factory for loggers created later in the run.</param>
    /// <param name="console">Console to write progress and results to.</param>
    protected CommandBase(ILogger<CommandBase> logger, ILoggerFactory loggerFactory, IConsole console)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _console = console;
    }

    /// <summary>Client for team project metadata.</summary>
    protected ProjectHttpClient _projectClient;

    /// <summary>Client for classic Build definitions.</summary>
    protected BuildHttpClient _buildClient;

    /// <summary>Client for classic Release definitions.</summary>
    protected ReleaseHttpClient _releaseClient;

    /// <summary>Client for task groups and variable groups.</summary>
    protected TaskAgentHttpClient _taskAgentClient;

    /// <summary>Credential built from the supplied access token.</summary>
    protected VssBasicCredential _credentials;

    /// <summary>Connection every client above is created from.</summary>
    protected VssConnection _connection;

    /// <summary>The team project being converted.</summary>
    protected TeamProject _project;

    /// <summary>Build definition references, which carry a name and identifier but no process.</summary>
    protected List<BuildDefinitionReference> buildDefinitionReferences;

    /// <summary>Fully loaded build definitions.</summary>
    /// <remarks>Appended to from parallel loops, so it must be a concurrent collection.</remarks>
    protected ConcurrentBag<BuildDefinition> buildDefinitions;

    /// <summary>Fully loaded release definitions.</summary>
    protected List<ReleaseDefinition> releaseDefinitions;

    /// <summary>Progress bar for the current top-level operation.</summary>
    protected ProgressBar pbar;

    /// <summary>Appearance of <see cref="pbar"/>.</summary>
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

    /// <summary>Progress bar nested inside <see cref="pbar"/>.</summary>
    protected ChildProgressBar childPBar;

    /// <summary>Appearance of <see cref="childPBar"/>.</summary>
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

    /// <summary>Resolves the named team project and stores it in <see cref="_project"/>.</summary>
    /// <param name="project">Team project name.</param>
    /// <param name="cancellationToken">Token to cancel the lookup.</param>
    /// <returns>True when the project was found, otherwise false.</returns>
    protected async Task<bool> GetProjectAsync(string project, CancellationToken cancellationToken = default)
    {
        _console.Write($"Retrieving Azure DevOps Project '{project}' ... ");
        try
        {
            _project = await _projectClient.GetProject(project);
        }
        catch (Exception ex)
        {
            _console.WriteLine();
            _logger.LogError(ex, "{ClassName} could not retrieve project {Project}", nameof(CommandBase), project);
            _console.WriteLine($"Unable to retrieve project '{project}': {ex.Message}");
            return false;
        }
        if (_project is not null)
            _console.WriteLine($" retrieved :)");
        else
            _console.WriteLine($" not found :(");
        return _project is not null;
    }

    /// <remarks>
    /// The credential is validated by connecting, never by inspecting its length. A pipeline-issued
    /// access token is a different length from a Personal Access Token - see
    /// <see href="https://github.com/f2calv/yamlizr/issues/181" />.
    /// </remarks>
    protected async Task<bool> ConnectAsync(string accessToken, Uri organisationUri, CancellationToken cancellationToken = default)
    {
        _console.Write($"Connecting to Azure DevOps REST API, {organisationUri} ...");
        try
        {
            _credentials = new VssBasicCredential(string.Empty, accessToken);
            _connection = new VssConnection(organisationUri, _credentials);
            await _connection.ConnectAsync(cancellationToken);
            _projectClient = _connection.GetClient<ProjectHttpClient>();
            _buildClient = _connection.GetClient<BuildHttpClient>();
            _releaseClient = _connection.GetClient<ReleaseHttpClient>();
            _taskAgentClient = _connection.GetClient<TaskAgentHttpClient>();
            _apiSvc = new ApiService(_loggerFactory.CreateLogger<ApiService>(), accessToken);
        }
        catch (Exception ex)
        {
            _console.WriteLine();
            _logger.LogError(ex, "{ClassName} could not authenticate against {OrganisationUri}", nameof(CommandBase), organisationUri);
            _console.WriteLine($"Unable to authenticate with the Azure DevOps REST API: {ex.Message}");
            _console.WriteLine("Check the organisation Uri, and that the token grants Build (Read), Release (Read), Task Groups (Read) and Variable Groups (Read).");
            return false;
        }
        _console.WriteLine($" connected :)");
        return true;
    }
}
