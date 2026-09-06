namespace CasCap.Models;

/// <summary>Identifies one version of a task group.</summary>
/// <remarks>
/// Used as a dictionary key while converting. A single definition may reference several versions of
/// the same task group, and each version produces its own template file, so the identifier alone is
/// not enough to key one.
/// </remarks>
public readonly struct TaskGroupVersion
{
    /// <summary>Creates a key for a specific version of a task group.</summary>
    /// <param name="_taskGroupId">Identifier of the task group.</param>
    /// <param name="_version">Major version the referencing step pinned.</param>
    public TaskGroupVersion(Guid _taskGroupId, int _version)
    {
        taskGroupId = _taskGroupId;
        version = _version;
    }

    /// <summary>Identifier of the task group.</summary>
    public Guid taskGroupId { get; }

    /// <summary>Major version the referencing step pinned.</summary>
    public int version { get; }
}
