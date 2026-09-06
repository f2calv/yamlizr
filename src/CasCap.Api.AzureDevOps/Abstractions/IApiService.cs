using CasCap.Models;

namespace CasCap.Abstractions;

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
