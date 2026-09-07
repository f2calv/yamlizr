using CasCap.Abstractions;
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
    protected IApiService ApiSvc => _apiSvc ?? throw NotConnected();
    private IApiService? _apiSvc;

    /// <summary>Initialises the shared dependencies.</summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="loggerFactory">Factory for loggers created later in the run.</param>
    /// <param name="console">Console to write progress and results to.</param>
    //TODO(#373): the suppression covers pbar and childPBar only, and CS8618 is reported here rather
    //than at the field, so it has to sit on the constructor. It goes when the progress bar does.
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    protected CommandBase(ILogger<CommandBase> logger, ILoggerFactory loggerFactory, IConsole console)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _console = console;
    }

    //Everything below is created by ConnectAsync, so it is absent until that has succeeded. Each is
    //read through an accessor that says so, rather than presenting a field that looks always present.
    private static InvalidOperationException NotConnected([System.Runtime.CompilerServices.CallerMemberName] string? member = null)
        => new($"'{member}' is not available because the command has not connected to Azure DevOps.");

    /// <summary>Client for team project metadata.</summary>
    protected ProjectHttpClient ProjectClient => _projectClient ?? throw NotConnected();
    private ProjectHttpClient? _projectClient;

    /// <summary>Client for classic Build definitions.</summary>
    protected BuildHttpClient BuildClient => _buildClient ?? throw NotConnected();
    private BuildHttpClient? _buildClient;

    /// <summary>Client for classic Release definitions.</summary>
    protected ReleaseHttpClient ReleaseClient => _releaseClient ?? throw NotConnected();
    private ReleaseHttpClient? _releaseClient;

    /// <summary>Client for task groups and variable groups.</summary>
    protected TaskAgentHttpClient TaskAgentClient => _taskAgentClient ?? throw NotConnected();
    private TaskAgentHttpClient? _taskAgentClient;

    /// <summary>Connection every client above is created from.</summary>
    protected VssConnection Connection => _connection ?? throw NotConnected();
    private VssConnection? _connection;

    /// <summary>The team project being converted.</summary>
    protected TeamProject Project => _project ?? throw NotConnected();
    private TeamProject? _project;

    /// <summary>Build definition references, which carry a name and identifier but no process.</summary>
    protected List<BuildDefinitionReference> buildDefinitionReferences = [];

    /// <summary>Fully loaded build definitions.</summary>
    /// <remarks>Appended to from parallel loops, so it must be a concurrent collection.</remarks>
    protected ConcurrentBag<BuildDefinition> buildDefinitions = [];

    /// <summary>Fully loaded release definitions.</summary>
    protected List<ReleaseDefinition> releaseDefinitions = [];

    //TODO(#373): ShellProgressBar cannot express a bar that does not exist yet, and every use site
    //assigns one immediately before using it. Annotating the two fields would put a null test on all
    //forty use sites for a type that issue #373 proposes deleting outright.
    //https://github.com/f2calv/yamlizr/issues/373
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
            _project = await ProjectClient.GetProject(project);
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
            var credentials = new VssBasicCredential(string.Empty, accessToken);
            _connection = new VssConnection(organisationUri, credentials);
            await Connection.ConnectAsync(cancellationToken);
            _projectClient = await Connection.GetClientAsync<ProjectHttpClient>(cancellationToken);
            _buildClient = await Connection.GetClientAsync<BuildHttpClient>(cancellationToken);
            _releaseClient = await Connection.GetClientAsync<ReleaseHttpClient>(cancellationToken);
            _taskAgentClient = await Connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);
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
