using CasCap.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
namespace CasCap.Services;

public interface IApiService
{
    Task<List<TaskObj>> GetAllExtensions(string organisationUri);
    Task<string> Validate(string organisation, string project, int pipelineId, string pipelineYaml);
}

public class ApiService : HttpClientBase, IApiService
{
    public ApiService(ILogger<ApiService> logger, string PAT) : base()
    {
        _logger = logger;
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var bytes = Encoding.ASCII.GetBytes($"{string.Empty}:{PAT}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    public async Task<List<TaskObj>> GetAllExtensions(string organisationUri)
    {
        _logger.LogInformation("Retrieving all extensions for organisation '{organisation}'", organisationUri);
        var res = await Get<Tasks, object>($"{organisationUri}/_apis/distributedtask/tasks/");
        return res.result is not null && res.result is not null && res.result.value is not null ? res.result.value : null;
    }

    //https://docs.microsoft.com/en-us/rest/api/azure/devops/pipelines/runs/run%20pipeline?view=azure-devops-rest-6.0
    public async Task<string> Validate(string organisation, string project, int pipelineId, string pipelineYaml)
    {
        _logger.LogInformation("Validating YAML for project '{project}' in organisation '{organisation}'", project, organisation);
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
