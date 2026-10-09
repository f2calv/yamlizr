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
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="PAT">
/// Personal Access Token, or an access token issued to a pipeline's build service identity.
/// Never validated by length, because the two differ; an invalid token surfaces as a failed call.
/// </param>
public partial class ApiService(ILogger<ApiService> logger, string PAT)
    : HttpClientBase(logger, CreateClient(PAT)), IApiService
{
    private static HttpClient CreateClient(string PAT)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.SetBasicAuth(string.Empty, PAT);
        return client;
    }

    /// <inheritdoc/>
    public async Task<List<TaskObj>?> GetAllExtensions(string organisationUri, CancellationToken cancellationToken = default)
    {
        LogRetrievingExtensions(_logger, nameof(ApiService), organisationUri);
        var (result, _, _, _) = await Get<Tasks, object>($"{organisationUri}/_apis/distributedtask/tasks/", cancellationToken: cancellationToken);
        return result?.Value;
    }

    /// <inheritdoc/>
    public async Task<PipelineValidationResult> Validate(
        string organisationUri,
        string project,
        int pipelineId,
        string pipelineYaml,
        CancellationToken cancellationToken = default)
    {
        LogValidatingPipelineYaml(_logger, nameof(ApiService), pipelineId, project);

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

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "{ClassName} retrieving all extensions for organisation '{OrganisationUri}'")]
    private static partial void LogRetrievingExtensions(ILogger logger, string className, string organisationUri);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "{ClassName} validating YAML against pipeline {PipelineId} in project '{Project}'")]
    private static partial void LogValidatingPipelineYaml(ILogger logger, string className, int pipelineId, string project);

    private static string? ReadProperty(string json, string name)
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
