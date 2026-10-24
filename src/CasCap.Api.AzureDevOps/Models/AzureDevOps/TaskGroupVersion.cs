namespace CasCap.Models;

/// <summary>Identifies one version of a task group.</summary>
/// <remarks>
/// Used as a dictionary key while converting. A single definition may reference several versions of
/// the same task group, and each version produces its own template file, so the identifier alone is
/// not enough to key one.
/// </remarks>
public readonly struct TaskGroupVersion(Guid taskGroupId, int version)
{
    /// <summary>Identifier of the task group.</summary>
    public Guid TaskGroupId { get; } = taskGroupId;

    /// <summary>Major version the referencing step pinned.</summary>
    public int Version { get; } = version;
}
