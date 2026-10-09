namespace CasCap.Models;

/// <summary>Version of an Azure Pipelines task, as reported by the organisation's task catalogue.</summary>
/// <remarks>
/// A classic step pins a task by major version only, through a <c>N.*</c> version spec, so the minor
/// and patch components describe what the organisation currently has installed rather than what the
/// definition asked for.
/// </remarks>
public class TaskVersion
{
    /// <summary>Major version, the only component a classic version spec pins.</summary>
    [JsonPropertyName("major")]
    public int Major { get; set; }

    /// <summary>Minor version of the installed task.</summary>
    [JsonPropertyName("minor")]
    public int Minor { get; set; }

    /// <summary>Patch version of the installed task.</summary>
    [JsonPropertyName("patch")]
    public int Patch { get; set; }
}
