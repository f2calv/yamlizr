namespace CasCap.Models;

/// <summary>One task in the organisation's task catalogue.</summary>
/// <remarks>
/// A classic step names a task only by <see cref="Id"/> and a version spec, so the catalogue is what
/// turns that into the <c>Task@Major</c> reference a YAML step needs. A step whose task is absent
/// from the catalogue cannot be converted, which is the reported case where an extension supplying
/// it was uninstalled.
/// </remarks>
public class TaskObj
{
    /// <summary>True when the task's implementation has been uploaded to the organisation.</summary>
    [JsonPropertyName("contentsUploaded")]
    public bool ContentsUploaded { get; set; }

    /// <summary>True when the task still runs but should no longer be used in new definitions.</summary>
    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; set; }

    /// <summary>True when the task is published as a preview.</summary>
    [JsonPropertyName("preview")]
    public bool Preview { get; set; }

    /// <summary>True when the task ships with Azure DevOps rather than an installed extension.</summary>
    [JsonPropertyName("serverOwned")]
    public bool ServerOwned { get; set; }

    /// <summary>True when the task's inputs are also exposed to the step as environment variables.</summary>
    [JsonPropertyName("showEnvironmentVariables")]
    public bool ShowEnvironmentVariables { get; set; }

    /// <summary>The entries of <see cref="Inputs"/> keyed by <see cref="TaskInput.Name"/>, for lookup.</summary>
    [JsonIgnore]
    public Dictionary<string, TaskInput>? InputMap { get; set; }

    /// <summary>Identifier a classic step uses to reference this task.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>Agent capabilities the task requires.</summary>
    [JsonPropertyName("demands")]
    public List<string>? Demands { get; set; }

    /// <summary>Execution targets the task supports, for example <c>Agent</c> or <c>DeploymentGroup</c>.</summary>
    [JsonPropertyName("runsOn")]
    public List<string>? RunsOn { get; set; }

    /// <summary>Demands this task satisfies for later tasks in the same job.</summary>
    [JsonPropertyName("satisfies")]
    public List<string>? Satisfies { get; set; }

    /// <summary>Definition types the task may appear in, for example <c>Build</c> or <c>Release</c>.</summary>
    [JsonPropertyName("visibility")]
    public List<string>? Visibility { get; set; }

    /// <summary>Inputs the task declares.</summary>
    [JsonPropertyName("inputs")]
    public List<TaskInput>? Inputs { get; set; }

    /// <summary>Publisher of the task.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>Category the task is grouped under in the classic editor, for example <c>Build</c>.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Identifier of the extension contribution supplying the task, when it is not in-box.</summary>
    [JsonPropertyName("contributionIdentifier")]
    public string? ContributionIdentifier { get; set; }

    /// <summary>Version of the extension contribution supplying the task.</summary>
    [JsonPropertyName("contributionVersion")]
    public string? ContributionVersion { get; set; }

    /// <summary>Kind of definition this entry describes, which is <c>task</c> for a task and <c>metaTask</c> for a task group.</summary>
    [JsonPropertyName("definitionType")]
    public string? DefinitionType { get; set; }

    /// <summary>Description shown in the classic editor.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Display name shown in the classic editor, which differs from <see cref="Name"/> for several in-box tasks.</summary>
    [JsonPropertyName("friendlyName")]
    public string? FriendlyName { get; set; }

    /// <summary>Help text shown beside the task, in Markdown.</summary>
    [JsonPropertyName("helpMarkDown")]
    public string? HelpMarkDown { get; set; }

    /// <summary>Link to the task's documentation.</summary>
    [JsonPropertyName("helpUrl")]
    public string? HelpUrl { get; set; }

    /// <summary>Link to the icon shown beside the task.</summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>Template producing a step's display name when the definition supplies none.</summary>
    [JsonPropertyName("instanceNameFormat")]
    public string? InstanceNameFormat { get; set; }

    /// <summary>Lowest agent version able to run the task.</summary>
    [JsonPropertyName("minimumAgentVersion")]
    public string? MinimumAgentVersion { get; set; }

    /// <summary>Name emitted in a YAML step reference, as the <c>Name</c> half of <c>Name@Major</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Release notes for the installed version.</summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>Version installed in the organisation.</summary>
    [JsonPropertyName("version")]
    public TaskVersion? Version { get; set; }
}
