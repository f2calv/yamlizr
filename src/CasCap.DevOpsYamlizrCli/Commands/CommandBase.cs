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
    //appended-to from parallel loops, so it must be a concurrent collection
    protected ConcurrentBag<BuildDefinition> buildDefinitions;
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
