using CasCap.Services;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ExtensionManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.ServiceEndpoints.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using ShellProgressBar;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
namespace CasCap.Commands
{
    /// <summary>
    /// This base type provides shared functionality.
    /// Also, declaring <see cref="HelpOptionAttribute"/> on this type means all types that inherit from it
    /// will automatically support '--help'
    /// </summary>
    [HelpOption("--help")]
    public abstract class CommandBase
    {
        protected /*readonly*/ ILogger _logger;
        protected ProjectHttpClient _projectClient;
        protected BuildHttpClient _buildClient;
        protected ReleaseHttpClient _releaseClient;
        protected TaskAgentHttpClient _taskAgentClient;
        protected ServiceEndpointHttpClient _endpointClient;
        protected ExtensionManagementHttpClient _extensionClient;
        protected ApiService _apiService;

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
            Console.Write($"Retrieving Azure DevOps Project '{project}' ... ");
            try
            {
                _project = await _projectClient.GetProject(project);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            if (_project is object)
                Console.WriteLine($" retrieved :)");
            else
                Console.Write($" not found :(");
            return _project is object;
        }

        protected bool Connect(string PAT, string organisation)
        {
            var uriString = $"https://dev.azure.com/{organisation}";
            Console.Write($"Connecting to Azure DevOps REST API, {uriString} ...");
            try
            {
                _credentials = new VssBasicCredential(string.Empty, PAT);
                _connection = new VssConnection(new Uri(uriString), _credentials);
                _projectClient = _connection.GetClient<ProjectHttpClient>();
                _buildClient = _connection.GetClient<BuildHttpClient>();
                _releaseClient = _connection.GetClient<ReleaseHttpClient>();
                _taskAgentClient = _connection.GetClient<TaskAgentHttpClient>();
                _extensionClient = _connection.GetClient<ExtensionManagementHttpClient>();
                _endpointClient = _connection.GetClient<ServiceEndpointHttpClient>();
                _apiService = new ApiService(PAT);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Console.WriteLine($"Unable to authenticate with Azure DevOps REST API :(");
                return false;
            }
            Console.WriteLine($" connected :)");
            return true;
        }
    }
}