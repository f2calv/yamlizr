using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace CasCap.Models;

/// <summary>A step template generated from a classic task group.</summary>
/// <remarks>
/// One file is produced per task group version and referenced by every definition that uses it,
/// unless <c>--inline</c> was passed, in which case the task group's steps are expanded in place.
/// </remarks>
public class Template : Pipeline
{
    /// <summary>The task group this template was generated from.</summary>
    /// <remarks>Carried for the file name and version only, so it is never serialised.</remarks>
    [YamlIgnore]
    public TaskGroup taskGroup { get; set; }
}
