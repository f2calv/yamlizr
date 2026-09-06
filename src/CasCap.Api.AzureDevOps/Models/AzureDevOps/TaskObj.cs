namespace CasCap.Models;

/// <summary>One task in the organisation's task catalogue.</summary>
/// <remarks>
/// A classic step names a task only by <see cref="id"/> and a version spec, so the catalogue is what
/// turns that into the <c>Task@Major</c> reference a YAML step needs. A step whose task is absent
/// from the catalogue cannot be converted, which is the reported case where an extension supplying
/// it was uninstalled.
/// </remarks>
public class TaskObj
{
    /// <summary>True when the task's implementation has been uploaded to the organisation.</summary>
    public bool contentsUploaded { get; set; }

    /// <summary>True when the task still runs but should no longer be used in new definitions.</summary>
    public bool deprecated { get; set; }

    /// <summary>True when the task is published as a preview.</summary>
    public bool preview { get; set; }

    /// <summary>True when the task ships with Azure DevOps rather than an installed extension.</summary>
    public bool serverOwned { get; set; }

    /// <summary>True when the task's inputs are also exposed to the step as environment variables.</summary>
    public bool showEnvironmentVariables { get; set; }

    /// <summary>The entries of <see cref="inputs"/> keyed by <see cref="TaskInput.name"/>, for lookup.</summary>
    public Dictionary<string, TaskInput> inputMap { get; set; }

    /// <summary>Identifier a classic step uses to reference this task.</summary>
    public Guid id { get; set; }

    /// <summary>Agent capabilities the task requires.</summary>
    public List<string> demands { get; set; }

    /// <summary>Execution targets the task supports, for example <c>Agent</c> or <c>DeploymentGroup</c>.</summary>
    public List<string> runsOn { get; set; }

    /// <summary>Demands this task satisfies for later tasks in the same job.</summary>
    public List<string> satisfies { get; set; }

    /// <summary>Definition types the task may appear in, for example <c>Build</c> or <c>Release</c>.</summary>
    public List<string> visibility { get; set; }

    /// <summary>Inputs the task declares.</summary>
    public List<TaskInput> inputs { get; set; }

    /// <summary>Publisher of the task.</summary>
    public string author { get; set; }

    /// <summary>Category the task is grouped under in the classic editor, for example <c>Build</c>.</summary>
    public string category { get; set; }

    /// <summary>Identifier of the extension contribution supplying the task, when it is not in-box.</summary>
    public string contributionIdentifier { get; set; }

    /// <summary>Version of the extension contribution supplying the task.</summary>
    public string contributionVersion { get; set; }

    /// <summary>Kind of definition this entry describes, which is <c>task</c> for a task and <c>metaTask</c> for a task group.</summary>
    public string definitionType { get; set; }

    /// <summary>Description shown in the classic editor.</summary>
    public string description { get; set; }

    /// <summary>Display name shown in the classic editor, which differs from <see cref="name"/> for several in-box tasks.</summary>
    public string friendlyName { get; set; }

    /// <summary>Help text shown beside the task, in Markdown.</summary>
    public string helpMarkDown { get; set; }

    /// <summary>Link to the task's documentation.</summary>
    public string helpUrl { get; set; }

    /// <summary>Link to the icon shown beside the task.</summary>
    public string iconUrl { get; set; }

    /// <summary>Template producing a step's display name when the definition supplies none.</summary>
    public string instanceNameFormat { get; set; }

    /// <summary>Lowest agent version able to run the task.</summary>
    public string minimumAgentVersion { get; set; }

    /// <summary>Name emitted in a YAML step reference, as the <c>Name</c> half of <c>Name@Major</c>.</summary>
    public string name { get; set; }

    /// <summary>Release notes for the installed version.</summary>
    public string releaseNotes { get; set; }

    /// <summary>Version installed in the organisation.</summary>
    public TaskVersion version { get; set; }
}
