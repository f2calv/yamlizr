using System.ComponentModel.DataAnnotations;

namespace CasCap.Models;

/// <summary>
/// Azure DevOps connection details bound from the <c>CasCap:AzureDevOpsOptions</c> configuration section.
/// </summary>
/// <remarks>
/// Every value may also be supplied as a command line option, which always takes precedence. Configuration
/// exists so an unattended run does not have to put a credential on a process command line.
/// </remarks>
public record AzureDevOpsOptions
{
    /// <summary>Configuration section that binds to this record.</summary>
    public const string ConfigurationSectionName = $"{nameof(CasCap)}:{nameof(AzureDevOpsOptions)}";

    /// <summary>Personal Access Token, or an access token issued to a pipeline's build service identity.</summary>
    /// <remarks>Never validated by length; a pipeline access token is a different length from a PAT.</remarks>
    [MinLength(1)]
    public string PAT { get; init; }

    /// <summary>Absolute Uri of the Azure DevOps organisation, e.g. <c>https://dev.azure.com/myorg</c>.</summary>
    [Url]
    public string OrganisationUri { get; init; }

    /// <summary>Name of the Azure DevOps team project to convert.</summary>
    [MinLength(1)]
    public string Project { get; init; }

    /// <summary>Identifier of a YAML pipeline used only as a target for validating generated YAML.</summary>
    /// <remarks>
    /// The pipeline preview endpoint needs an existing, enabled YAML pipeline to parse against, and
    /// its own YAML is replaced by the document being validated. Null disables validation.
    /// <para>Created by <c>.scripts/New-FixtureDefinitions.ps1</c>.</para>
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int? ValidationPipelineId { get; init; }
}
