using CasCap.Abstractions;
using CasCap.Common.Extensions;
using CasCap.Common.Services;
using CasCap.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CasCap.Services;

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
        Client.SetBasicAuth(string.Empty, PAT);
    }

    /// <inheritdoc/>
    public async Task<List<TaskObj>> GetAllExtensions(string organisationUri)
    {
        _logger.LogInformation("{ClassName} retrieving all extensions for organisation '{OrganisationUri}'",
            nameof(ApiService), organisationUri);
        var res = await Get<Tasks, object>($"{organisationUri}/_apis/distributedtask/tasks/");
        return res.result?.value;
    }

    /// <inheritdoc/>
    public async Task<PipelineValidationResult> Validate(
        string organisationUri,
        string project,
        int pipelineId,
        string pipelineYaml,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{ClassName} validating YAML against pipeline {PipelineId} in project '{Project}'",
            nameof(ApiService), pipelineId, project);

        var uri = $"{organisationUri.TrimEnd('/')}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/preview?api-version=7.1";
        var payload = JsonSerializer.Serialize(new { previewRun = true, yamlOverride = pipelineYaml });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(uri, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
            return new PipelineValidationResult { IsValid = true, FinalYaml = ReadProperty(body, "finalYaml") };

        //the rejection reason is the whole point of the call, so fall back to the status when absent
        var message = ReadProperty(body, "message") ?? $"Azure DevOps returned {(int)response.StatusCode}.";
        _logger.LogWarning("{ClassName} rejected YAML for pipeline {PipelineId}, {Message}",
            nameof(ApiService), pipelineId, message);

        return new PipelineValidationResult { IsValid = false, Message = message };
    }

    private static string ReadProperty(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
