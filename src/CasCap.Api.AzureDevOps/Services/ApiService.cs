using CasCap.Common.Services;
using CasCap.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;

namespace CasCap.Services;

/// <summary>Azure DevOps REST calls that the official client libraries do not cover.</summary>
public interface IApiService
{
    /// <summary>Retrieves every task installed in the organisation.</summary>
    /// <remarks>
    /// The classic definition names a task only by identifier and version spec, so this catalogue is
    /// what resolves it to the <c>Name@Major</c> reference a YAML step needs.
    /// </remarks>
    /// <param name="organisationUri">Absolute organisation Uri, for example <c>https://dev.azure.com/myorg</c>.</param>
    /// <returns>Every installed task, or null when the response carried none.</returns>
    Task<List<TaskObj>> GetAllExtensions(string organisationUri);

    /// <summary>Asks Azure DevOps to validate a YAML document without queueing a run.</summary>
    /// <param name="organisation">Organisation name, the segment after <c>dev.azure.com/</c>.</param>
    /// <param name="project">Team project name.</param>
    /// <param name="pipelineId">Identifier of an existing YAML pipeline to preview against.</param>
    /// <param name="pipelineYaml">The document to validate.</param>
    /// <returns>The preview response body, or null when the call returned none.</returns>
    Task<string> Validate(string organisation, string project, int pipelineId, string pipelineYaml);
}

/// <inheritdoc cref="IApiService"/>
public class ApiService : HttpClientBase, IApiService
{
    /// <summary>Creates a client authenticated against Azure DevOps.</summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="PAT">
    /// Personal Access Token, or an access token issued to a pipeline's build service identity.
    /// Never validated by length, because the two differ; an invalid token surfaces as a failed call.
    /// </param>
    public ApiService(ILogger<ApiService> logger, string PAT) : base()
    {
        _logger = logger;
        Client = new HttpClient();
        Client.DefaultRequestHeaders.Clear();
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var bytes = Encoding.ASCII.GetBytes($"{string.Empty}:{PAT}");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    /// <inheritdoc/>
    public async Task<List<TaskObj>> GetAllExtensions(string organisationUri)
    {
        _logger.LogInformation("{ClassName} retrieving all extensions for organisation '{OrganisationUri}'",
            nameof(ApiService), organisationUri);
        var res = await Get<Tasks, object>($"{organisationUri}/_apis/distributedtask/tasks/");
        return res.result is not null && res.result.value is not null ? res.result.value : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// See <see href="https://docs.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/run%20pipeline?view=azure-devops-rest-6.0" />.
    /// </remarks>
    public async Task<string> Validate(string organisation, string project, int pipelineId, string pipelineYaml)
    {
        _logger.LogInformation("{ClassName} validating YAML for project '{Project}' in organisation '{Organisation}'",
            nameof(ApiService), project, organisation);
        var req = new
        {
            previewRun = true,
            yamlOverride = $@"
# your YAML here
{pipelineYaml}
"
        };
        var res = await PostJsonAsync<string, object>($"https://dev.azure.com/{organisation}/{project}/_apis/pipelines/{pipelineId}/runs?api-version=6.0-preview.1", req);
        return res.result is not null ? res.result : null;
    }
}
