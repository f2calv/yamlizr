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

    /// <summary>Asks Azure DevOps to parse a YAML document without queueing a run.</summary>
    /// <remarks>
    /// Wraps the pipeline preview endpoint, which expands templates and reports the first parse error.
    /// The target pipeline must exist, must be a YAML pipeline, and must be enabled; a disabled one
    /// fails the call outright. Its own YAML is irrelevant because <paramref name="pipelineYaml"/>
    /// replaces it.
    /// <para>
    /// Relative <c>template:</c> references resolve against the target pipeline's repository, not the
    /// submitted document, so generated YAML that references a task group template only validates
    /// when those templates are committed, or when it was generated with task groups inlined.
    /// </para>
    /// <para>See <see href="https://learn.microsoft.com/rest/api/azure/devops/pipelines/preview/preview" />.</para>
    /// </remarks>
    /// <param name="organisationUri">Absolute organisation Uri, for example <c>https://dev.azure.com/myorg</c>.</param>
    /// <param name="project">Team project name.</param>
    /// <param name="pipelineId">Identifier of an existing, enabled YAML pipeline to preview against.</param>
    /// <param name="pipelineYaml">The document to validate.</param>
    /// <param name="cancellationToken">Token to cancel the call.</param>
    /// <returns>Whether the document parsed, with either the expanded YAML or the rejection reason.</returns>
    Task<PipelineValidationResult> Validate(
        string organisationUri,
        string project,
        int pipelineId,
        string pipelineYaml,
        CancellationToken cancellationToken = default);
}
